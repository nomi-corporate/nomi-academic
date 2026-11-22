using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nomi.Academic.Infrastructure.Identity;
using Nomi.Academic.Infrastructure.Persistence;

namespace Nomi.Academic.Api.Tests;

public sealed class AcademicApiTests
{
    [Fact]
    public async Task SubjectCreate_ReplaysSameIntent_AndRejectsKeyReuseWithDifferentBody()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        var first = await CreateSubjectAsync(client, "user-a", "retry-key", "Cálculo");
        var replay = await CreateSubjectAsync(client, "user-a", "retry-key", "Cálculo");
        var firstValue = await first.Content.ReadFromJsonAsync<JsonElement>();
        var replayValue = await replay.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstValue.GetProperty("id").GetGuid(), replayValue.GetProperty("id").GetGuid());
        Assert.Equal(first.Headers.ETag?.Tag, replay.Headers.ETag?.Tag);

        var changed = await CreateSubjectAsync(client, "user-a", "retry-key", "Física");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        using var list = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/subjects");
        var listed = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listed.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ProfessorLinks_AreReplaceableAndIsolatedByAccount()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();

        using var professorResponse = await client.SendAsync(CreateProfessorRequest("user-a", "professor-key", new { name = "Ana", email = "ana@example.edu", office = "Sala 12" }));
        Assert.Equal(HttpStatusCode.Created, professorResponse.StatusCode);
        var professor = await professorResponse.Content.ReadFromJsonAsync<JsonElement>();
        var professorId = professor.GetProperty("id").GetGuid();
        using var professorList = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/professors?query=ANA");
        var listedProfessors = await professorList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listedProfessors.GetProperty("items").GetArrayLength());
        using var hiddenProfessor = await SendAsync(client, "user-b", HttpMethod.Get, $"/api/v1/professors/{professorId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenProfessor.StatusCode);
        var foreignProfessorPatch = JsonRequest("user-b", HttpMethod.Patch, $"/api/v1/professors/{professorId}", new { name = "Tentativa" });
        foreignProfessorPatch.Headers.TryAddWithoutValidation("If-Match", professorResponse.Headers.ETag!.Tag);
        using var hiddenProfessorPatch = await client.SendAsync(foreignProfessorPatch);
        Assert.Equal(HttpStatusCode.NotFound, hiddenProfessorPatch.StatusCode);
        var foreignProfessorArchive = JsonRequest("user-b", HttpMethod.Put, $"/api/v1/professors/{professorId}/archive", new { archived = true });
        foreignProfessorArchive.Headers.TryAddWithoutValidation("If-Match", professorResponse.Headers.ETag!.Tag);
        using var hiddenProfessorArchive = await client.SendAsync(foreignProfessorArchive);
        Assert.Equal(HttpStatusCode.NotFound, hiddenProfessorArchive.StatusCode);

        using var subjectResponse = await CreateSubjectAsync(client, "user-a", "subject-key", "Cálculo", new[] { professorId });
        Assert.Equal(HttpStatusCode.Created, subjectResponse.StatusCode);
        var subject = await subjectResponse.Content.ReadFromJsonAsync<JsonElement>();
        var subjectId = subject.GetProperty("id").GetGuid();
        Assert.Equal(professorId, subject.GetProperty("professorIds")[0].GetGuid());

        using var foreignGet = await SendAsync(client, "user-b", HttpMethod.Get, $"/api/v1/subjects/{subjectId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        var foreignPatch = JsonRequest("user-b", HttpMethod.Patch, $"/api/v1/subjects/{subjectId}", new { name = "Tentativa" });
        foreignPatch.Headers.TryAddWithoutValidation("If-Match", subjectResponse.Headers.ETag!.Tag);
        using var hiddenSubjectPatch = await client.SendAsync(foreignPatch);
        Assert.Equal(HttpStatusCode.NotFound, hiddenSubjectPatch.StatusCode);
        var foreignArchive = JsonRequest("user-b", HttpMethod.Put, $"/api/v1/subjects/{subjectId}/archive", new { archived = true });
        foreignArchive.Headers.TryAddWithoutValidation("If-Match", subjectResponse.Headers.ETag!.Tag);
        using var hiddenSubjectArchive = await client.SendAsync(foreignArchive);
        Assert.Equal(HttpStatusCode.NotFound, hiddenSubjectArchive.StatusCode);

        using var foreignProfessor = await client.SendAsync(CreateProfessorRequest("user-b", "other-professor-key", new { name = "Bruno" }));
        var otherProfessor = await foreignProfessor.Content.ReadFromJsonAsync<JsonElement>();
        var replacement = JsonRequest("user-a", HttpMethod.Put, $"/api/v1/subjects/{subjectId}/professors", new { professorIds = new[] { otherProfessor.GetProperty("id").GetGuid() } });
        replacement.Headers.TryAddWithoutValidation("If-Match", subjectResponse.Headers.ETag!.Tag);
        using var rejectedLink = await client.SendAsync(replacement);
        Assert.Equal(HttpStatusCode.NotFound, rejectedLink.StatusCode);

        var clear = JsonRequest("user-a", HttpMethod.Put, $"/api/v1/subjects/{subjectId}/professors", new { professorIds = Array.Empty<Guid>() });
        clear.Headers.TryAddWithoutValidation("If-Match", subjectResponse.Headers.ETag!.Tag);
        using var clearResponse = await client.SendAsync(clear);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        Assert.Equal("\"1\"", clearResponse.Headers.ETag!.Tag);
        var cleared = await clearResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(cleared.GetProperty("professorIds").EnumerateArray());
    }

    [Fact]
    public async Task ProfessorCanBeEditedAndArchivedWithVersionPreconditions()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        using var createdResponse = await client.SendAsync(CreateProfessorRequest("user-a", "professor-retry-key", new { name = "Ana", email = "ana@example.edu" }));
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        using var replay = await client.SendAsync(CreateProfessorRequest("user-a", "professor-retry-key", new { name = "Ana", email = "ana@example.edu" }));
        var replayValue = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(id, replayValue.GetProperty("id").GetGuid());
        using var changedIntent = await client.SendAsync(CreateProfessorRequest("user-a", "professor-retry-key", new { name = "Ana Silva", email = "ana@example.edu" }));
        Assert.Equal(HttpStatusCode.Conflict, changedIntent.StatusCode);
        using var oneProfessor = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/professors");
        Assert.Equal(1, (await oneProfessor.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());

        var patch = JsonRequest("user-a", HttpMethod.Patch, $"/api/v1/professors/{id}", new { name = "Ana Silva", office = "Sala 3" });
        patch.Headers.TryAddWithoutValidation("If-Match", createdResponse.Headers.ETag!.Tag);
        using var patchedResponse = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, patchedResponse.StatusCode);
        Assert.Equal("\"1\"", patchedResponse.Headers.ETag!.Tag);

        var stale = JsonRequest("user-a", HttpMethod.Put, $"/api/v1/professors/{id}/archive", new { archived = true });
        stale.Headers.TryAddWithoutValidation("If-Match", createdResponse.Headers.ETag!.Tag);
        using var staleResponse = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);

        var archive = JsonRequest("user-a", HttpMethod.Put, $"/api/v1/professors/{id}/archive", new { archived = true });
        archive.Headers.TryAddWithoutValidation("If-Match", patchedResponse.Headers.ETag!.Tag);
        using var archivedResponse = await client.SendAsync(archive);
        Assert.Equal(HttpStatusCode.OK, archivedResponse.StatusCode);
        var archived = await archivedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(archived.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task AnonymousRequestsAreRejectedAndInvalidProfessorEmailIsNotStored()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        using var anonymous = await client.GetAsync("/api/v1/professors");
        Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized, await anonymous.Content.ReadAsStringAsync());
        using var invalidBearer = await SendAsync(client, "invalid", HttpMethod.Get, "/api/v1/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidBearer.StatusCode);
        using var identityUnavailable = await SendAsync(client, "identity-down", HttpMethod.Get, "/api/v1/subjects");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, identityUnavailable.StatusCode);
        using var identityUnavailableCreate = JsonRequest("identity-down", HttpMethod.Post, "/api/v1/subjects", new { name = "Segredo" });
        identityUnavailableCreate.Headers.TryAddWithoutValidation("Idempotency-Key", "offline-key");
        using var notCreated = await client.SendAsync(identityUnavailableCreate);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notCreated.StatusCode);

        using var invalidEmail = await client.SendAsync(CreateProfessorRequest("user-a", "invalid-email-key", new { name = "Ana", email = "not-an-email" }));
        Assert.Equal((HttpStatusCode)422, invalidEmail.StatusCode);
    }

    [Fact]
    public async Task SubjectCreate_ReturnsConflictWhileMatchingIntentIsReserved()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var normalized = new { name = "Cálculo", code = (string?)null, colorToken = (string?)null, iconKey = (string?)null, professorIds = Array.Empty<Guid>() };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            db.IdempotencyRecords.Add(new Nomi.Academic.Domain.IdempotencyRecord(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "subjects.create", "pending-key", hash, now, now.AddMinutes(2)));
            await db.SaveChangesAsync();
        }

        using var response = await CreateSubjectAsync(client, "user-a", "pending-key", "Cálculo");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("request_in_progress", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Profile_PersistsWithVersionControl_AndKeepsIdentityEmailReadOnly()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        using var first = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var initial = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("user-a@example.test", initial.GetProperty("email").GetString());
        var patch = JsonRequest("user-a", HttpMethod.Patch, "/api/v1/me", new { displayName = "Ana", theme = "dark", timeZone = "Europe/Lisbon" });
        patch.Headers.TryAddWithoutValidation("If-Match", first.Headers.ETag!.Tag);
        using var saved = await client.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var stale = JsonRequest("user-a", HttpMethod.Patch, "/api/v1/me", new { displayName = "Obsoleto" });
        stale.Headers.TryAddWithoutValidation("If-Match", first.Headers.ETag.Tag);
        using var conflict = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, conflict.StatusCode);
        using var read = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/me");
        var profile = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ana", profile.GetProperty("displayName").GetString());
        Assert.Equal("dark", profile.GetProperty("theme").GetString());
        var forbiddenEmail = JsonRequest("user-a", HttpMethod.Patch, "/api/v1/me", new { email = "other@example.test" });
        forbiddenEmail.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.Tag);
        using var rejected = await client.SendAsync(forbiddenEmail);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var other = await SendAsync(client, "user-b", HttpMethod.Get, "/api/v1/me");
        var otherProfile = await other.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(profile.GetProperty("id").GetGuid(), otherProfile.GetProperty("id").GetGuid());
        Assert.Equal("system", otherProfile.GetProperty("theme").GetString());
    }

    [Theory]
    [InlineData("", "light", "America/Sao_Paulo")]
    [InlineData("Ana", "unknown", "America/Sao_Paulo")]
    [InlineData("Ana", "system", "Invalid/Zone")]
    [InlineData("Ana", "system", "E. South America Standard Time")]
    public async Task Profile_RejectsInvalidFieldsWithoutChangingSavedData(string name, string theme, string timeZone)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        using var first = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/me");
        var request = JsonRequest("user-a", HttpMethod.Patch, "/api/v1/me", new { displayName = name, theme, timeZone });
        request.Headers.TryAddWithoutValidation("If-Match", first.Headers.ETag!.Tag);
        using var response = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        using var read = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/me");
        Assert.Equal(first.Headers.ETag.Tag, read.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task AgendaAndHome_RespectCivilDatesTimeZonesOwnershipAndPendingStatus()
    {
        var now = DateTimeOffset.Parse("2026-11-20T15:00:00Z");
        using var factory = new ApiFactory(new FixedTimeProvider(now));
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        var owner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var day = new DateOnly(2026, 11, 20);
        var dateOnly = new Nomi.Academic.Domain.Commitment(owner, "Entrega sem hora", "assignment", null, day, "America/Sao_Paulo", now);
        var future = new Nomi.Academic.Domain.Commitment(owner, "Prova futura", "exam", null, day, "America/Sao_Paulo", now, DateTime.Parse("2026-11-20T17:00:00Z").ToUniversalTime());
        var past = new Nomi.Academic.Domain.Commitment(owner, "Prova atrasada", "exam", null, day, "America/Sao_Paulo", now, DateTime.Parse("2026-11-20T12:00:00Z").ToUniversalTime());
        var completed = new Nomi.Academic.Domain.Commitment(owner, "Concluída", "reminder", null, day, "America/Sao_Paulo", now, DateTime.Parse("2026-11-20T19:00:00Z").ToUniversalTime());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            db.Commitments.AddRange(dateOnly, future, past, completed,
                new(owner, "Entrega anterior", "assignment", null, day.AddDays(-1), "America/Sao_Paulo", now),
                new(other, "Privado", "reminder", null, day, "America/Sao_Paulo", now));
            db.Entry(completed).Property(x => x.Status).CurrentValue = "completed";
            await db.SaveChangesAsync();
        }
        const string range = "fromDate=2026-11-20&toDate=2026-11-21&timeZone=America%2FSao_Paulo";
        using var agenda = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/agenda?" + range);
        Assert.Equal(HttpStatusCode.OK, agenda.StatusCode);
        var listed = await agenda.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, listed.GetProperty("items").GetArrayLength());
        var dateItem = listed.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == dateOnly.Id);
        Assert.False(dateItem.GetProperty("due").TryGetProperty("instant", out _));
        Assert.Equal(JsonValueKind.Null, dateItem.GetProperty("displayTime").ValueKind);
        var timedItem = listed.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == future.Id);
        Assert.Equal("14:00", timedItem.GetProperty("displayTime").GetString());
        using var details = await SendAsync(client, "user-a", HttpMethod.Get, $"/api/v1/commitments/{future.Id}?timeZone=Pacific%2FHonolulu");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Equal("07:00", (await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayTime").GetString());
        using var invalidDetailsZone = await SendAsync(client, "user-a", HttpMethod.Get, $"/api/v1/commitments/{future.Id}?timeZone=Invalid%2FZone");
        Assert.Equal((HttpStatusCode)422, invalidDetailsZone.StatusCode);
        using var paged = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/agenda?" + range + "&limit=1");
        var next = (await paged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextCursor").GetString();
        Assert.NotNull(next);
        using var crossedCursor = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/agenda?fromDate=2026-11-20&toDate=2026-11-21&timeZone=UTC&cursor=" + Uri.EscapeDataString(next));
        Assert.Equal(HttpStatusCode.BadRequest, crossedCursor.StatusCode);
        using var home = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/home?date=2026-11-20&timeZone=America%2FSao_Paulo");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var summary = await home.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, summary.GetProperty("upcoming").GetArrayLength());
        Assert.Equal(2, summary.GetProperty("overdue").GetArrayLength());
        Assert.DoesNotContain(summary.GetProperty("upcoming").EnumerateArray(), x => x.GetProperty("id").GetGuid() == completed.Id);
        using var hidden = await SendAsync(client, "user-b", HttpMethod.Get, $"/api/v1/commitments/{future.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var otherAgenda = await SendAsync(client, "user-b", HttpMethod.Get, "/api/v1/agenda?" + range);
        Assert.Equal(1, (await otherAgenda.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("2026-11-20", "2027-01-01", "UTC")]
    [InlineData("2026-11-20", "2026-11-20", "UTC")]
    [InlineData("2026-11-20", "2026-11-21", "Invalid/Zone")]
    public async Task Agenda_RejectsInvalidRanges(string from, string to, string timeZone)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        using var response = await SendAsync(client, "user-a", HttpMethod.Get, $"/api/v1/agenda?fromDate={from}&toDate={to}&timeZone={Uri.EscapeDataString(timeZone)}");
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [Fact]
    public async Task GroupCreate_IsIdempotentAndReadableOnlyByActiveMember()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        var create = JsonRequest("user-a", HttpMethod.Post, "/api/v1/groups", new { name = "Estudo de Cálculo", description = "Turma A" });
        create.Headers.TryAddWithoutValidation("Idempotency-Key", "group-key");
        using var response = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var first = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = first.GetProperty("id").GetGuid();
        Assert.Equal("owner", first.GetProperty("role").GetString());

        var retry = JsonRequest("user-a", HttpMethod.Post, "/api/v1/groups", new { name = "Estudo de Cálculo", description = "Turma A" });
        retry.Headers.TryAddWithoutValidation("Idempotency-Key", "group-key");
        using var replay = await client.SendAsync(retry);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var changed = JsonRequest("user-a", HttpMethod.Post, "/api/v1/groups", new { name = "Outro grupo" });
        changed.Headers.TryAddWithoutValidation("Idempotency-Key", "group-key");
        using var conflict = await client.SendAsync(changed);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var ownList = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/groups");
        Assert.Equal(1, (await ownList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());
        using var otherList = await SendAsync(client, "user-b", HttpMethod.Get, "/api/v1/groups");
        Assert.Empty((await otherList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        using var hidden = await SendAsync(client, "user-b", HttpMethod.Get, $"/api/v1/groups/{id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var own = await SendAsync(client, "user-a", HttpMethod.Get, $"/api/v1/groups/{id}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var membership = await db.StudyGroupMemberships.SingleAsync(x => x.GroupId == id);
            db.Entry(membership).Property(x => x.Active).CurrentValue = false;
            await db.SaveChangesAsync();
        }
        using var revokedList = await SendAsync(client, "user-a", HttpMethod.Get, "/api/v1/groups");
        Assert.Empty((await revokedList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        using var revokedDetail = await SendAsync(client, "user-a", HttpMethod.Get, $"/api/v1/groups/{id}");
        Assert.Equal(HttpStatusCode.NotFound, revokedDetail.StatusCode);

        var invalid = JsonRequest("user-a", HttpMethod.Post, "/api/v1/groups", new { name = "  " });
        invalid.Headers.TryAddWithoutValidation("Idempotency-Key", "invalid-group-key");
        using var rejected = await client.SendAsync(invalid);
        Assert.Equal((HttpStatusCode)422, rejected.StatusCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Task<HttpResponseMessage> CreateSubjectAsync(HttpClient client, string user, string key, string name, Guid[]? professorIds = null)
    {
        var request = JsonRequest(user, HttpMethod.Post, "/api/v1/subjects", new { name, professorIds });
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static HttpRequestMessage CreateProfessorRequest(string user, string key, object body)
    {
        var request = JsonRequest(user, HttpMethod.Post, "/api/v1/professors", body);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return request;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string user, HttpMethod method, string path) =>
        client.SendAsync(JsonRequest(user, method, path));

    private static HttpRequestMessage JsonRequest(string user, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user);
        return request;
    }

    private sealed class ApiFactory(TimeProvider? clock = null) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private bool _initialized;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Academic"] = "" }));
            builder.ConfigureTestServices(services =>
            {
                if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
                services.AddSingleton(_connection);
                services.AddDbContext<AcademicDbContext>(options => options.UseSqlite(_connection));
                services.RemoveAll<IIdentityUserResolver>();
                services.AddSingleton<IIdentityUserResolver, TestIdentityUserResolver>();
            });
        }

        public async Task InitializeDatabaseAsync()
        {
            if (_initialized) return;
            await _connection.OpenAsync();
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Database.EnsureCreatedAsync();
            _initialized = true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    private sealed class TestIdentityUserResolver : IIdentityUserResolver
    {
        public Task<IdentityResolution> ResolveAsync(string? authorizationHeader, CancellationToken cancellationToken)
        {
            if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
                return Task.FromResult(new IdentityResolution(IdentityResolutionStatus.Unauthorized));
            var userId = header.Parameter switch
            {
                "user-a" => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "user-b" => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                _ => Guid.Empty
            };
            if (header.Parameter == "identity-down") return Task.FromResult(new IdentityResolution(IdentityResolutionStatus.Unavailable));
            return Task.FromResult(userId == Guid.Empty
                ? new IdentityResolution(IdentityResolutionStatus.Unauthorized)
                : new IdentityResolution(IdentityResolutionStatus.Authenticated, userId, $"{header.Parameter}@example.test", "Estudante"));
        }
    }
}
