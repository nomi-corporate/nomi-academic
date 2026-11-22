using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nomi.Academic.Domain;
using Nomi.Academic.Infrastructure.Identity;
using Nomi.Academic.Infrastructure.Persistence;

namespace Nomi.Academic.Api;

public static class ProfessorsEndpoints
{
    private const string CreateOperation = "professors.create";
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan IdempotencyLease = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProfessorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/professors");
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", PatchAsync);
        group.MapPut("/{id:guid}/archive", ArchiveAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, HttpResponse response, CreateProfessorRequest input,
        AcademicDbContext db, IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var idempotency) || string.IsNullOrWhiteSpace(idempotency))
            return SubjectsEndpoints.Problem(400, "idempotency_key_required", "Idempotency-Key é obrigatório para criar um professor.");
        var key = idempotency.ToString().Trim();
        if (key.Length > 128) return SubjectsEndpoints.Problem(400, "invalid_idempotency_key", "Idempotency-Key deve ter no máximo 128 caracteres.");
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var name = input.Name?.Trim();
        var email = SubjectsEndpoints.Normalize(input.Email);
        var office = SubjectsEndpoints.Normalize(input.Office);
        if (!SubjectsEndpoints.ValidateName(name, out var nameError)) return SubjectsEndpoints.Problem(422, "validation_failed", nameError!);
        if (!SubjectsEndpoints.ValidateLength(email, 254, "email", out var emailLengthError)) return SubjectsEndpoints.Problem(422, "validation_failed", emailLengthError!);
        if (email is not null && !IsValidEmail(email)) return SubjectsEndpoints.Problem(422, "validation_failed", "Informe um e-mail válido.");
        if (!SubjectsEndpoints.ValidateLength(office, 160, "office", out var officeError)) return SubjectsEndpoints.Problem(422, "validation_failed", officeError!);
        var now = clock.GetUtcNow();
        var normalized = new NormalizedCreateProfessor(name!, email, office);
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));
        await db.IdempotencyRecords.Where(x => x.CreatedAt <= now - IdempotencyRetention).ExecuteDeleteAsync(ct);
        var existing = await FindIdempotencyAsync(db, auth.UserId!.Value, key, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) return KeyReused();
            if (existing.Status == "completed") return Replay(existing, response);
            if (existing.LeaseExpiresAt > now) return InProgress();
            var oldLease = existing.LeaseExpiresAt;
            var reclaimed = await db.IdempotencyRecords.Where(x => x.OwnerUserId == auth.UserId && x.Operation == CreateOperation && x.Key == key && x.Status == "pending" && x.LeaseExpiresAt == oldLease)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseExpiresAt, now + IdempotencyLease), ct);
            if (reclaimed == 0) return InProgress();
        }
        else
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord(auth.UserId!.Value, CreateOperation, key, requestHash, now, now + IdempotencyLease));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                existing = await FindIdempotencyAsync(db, auth.UserId!.Value, key, ct);
                if (existing is null) throw;
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) return KeyReused();
                return existing.Status == "completed" ? Replay(existing, response) : InProgress();
            }
        }

        var professor = new Professor(auth.UserId!.Value, name!, email, office, now);
        var professorResponse = ToResponse(professor);
        var responseJson = JsonSerializer.Serialize(professorResponse, JsonOptions);
        var etag = SubjectsEndpoints.Etag(professor.Version);
        var location = $"/api/v1/professors/{professor.Id}";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Professors.Add(professor);
        var record = await db.IdempotencyRecords.SingleAsync(x => x.OwnerUserId == auth.UserId && x.Operation == CreateOperation && x.Key == key, ct);
        record.Complete(responseJson, etag, location);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            existing = await FindIdempotencyAsync(db, auth.UserId!.Value, key, ct);
            if (existing is null) throw;
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) return KeyReused();
            return existing.Status == "completed" ? Replay(existing, response) : InProgress();
        }
        response.Headers.ETag = SubjectsEndpoints.Etag(professor.Version);
        response.Headers.Location = location;
        return Results.Content(responseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(HttpRequest request, bool? archived, string? query, string? cursor, int? limit,
        AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var pageSize = Math.Clamp(limit ?? 50, 1, 100);
        ProfessorCursor? after = null;
        if (!string.IsNullOrWhiteSpace(cursor) && !TryDecode(cursor, out after)) return SubjectsEndpoints.Problem(400, "invalid_cursor", "O cursor informado é inválido.");
        var includeArchived = archived ?? false;
        var queryable = db.Professors.AsNoTracking().Where(x => x.OwnerUserId == auth.UserId && x.Archived == includeArchived);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var normalizedTerm = term.ToLowerInvariant();
            queryable = queryable.Where(x => x.Name.ToLower().Contains(normalizedTerm));
        }
        if (after is not null)
            queryable = queryable.Where(x => string.Compare(x.Name, after.Name) > 0 || (x.Name == after.Name && x.Id.CompareTo(after.Id) > 0));
        var page = await queryable.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(pageSize + 1).ToListAsync(ct);
        var hasMore = page.Count > pageSize;
        if (hasMore) page.RemoveAt(pageSize);
        var next = hasMore ? Encode(new(page[^1].Name, page[^1].Id)) : null;
        return Results.Ok(new ProfessorListResponse(page.Select(ToResponse).ToArray(), next));
    }

    private static async Task<IResult> GetAsync(HttpRequest request, Guid id, AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var professor = await db.Professors.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == auth.UserId && x.Id == id, ct);
        if (professor is null) return Results.NotFound();
        request.HttpContext.Response.Headers.ETag = SubjectsEndpoints.Etag(professor.Version);
        return Results.Ok(ToResponse(professor));
    }

    private static async Task<IResult> PatchAsync(HttpRequest request, Guid id, JsonElement input, AcademicDbContext db,
        IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        if (input.ValueKind != JsonValueKind.Object) return SubjectsEndpoints.Problem(400, "invalid_body", "O corpo precisa ser um objeto JSON.");
        var allowed = new[] { "name", "email", "office" };
        if (input.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal))) return SubjectsEndpoints.Problem(400, "unknown_field", "O corpo contém campo não permitido.");
        var professor = await db.Professors.SingleOrDefaultAsync(x => x.OwnerUserId == auth.UserId && x.Id == id, ct);
        if (professor is null) return Results.NotFound();
        if (!SubjectsEndpoints.TryIfMatch(request, professor.Version, out var precondition)) return precondition!;
        var name = professor.Name;
        if (input.TryGetProperty("name", out var nameElement))
        {
            if (nameElement.ValueKind != JsonValueKind.String) return SubjectsEndpoints.Problem(422, "validation_failed", "Nome inválido.");
            name = nameElement.GetString()!.Trim();
            if (!SubjectsEndpoints.ValidateName(name, out var error)) return SubjectsEndpoints.Problem(422, "validation_failed", error!);
        }
        var email = SubjectsEndpoints.ReadNullableString(input, "email", professor.Email, 254, out var emailError);
        if (emailError is not null) return SubjectsEndpoints.Problem(422, "validation_failed", emailError);
        if (email is not null && !IsValidEmail(email)) return SubjectsEndpoints.Problem(422, "validation_failed", "Informe um e-mail válido.");
        var office = SubjectsEndpoints.ReadNullableString(input, "office", professor.Office, 160, out var officeError);
        if (officeError is not null) return SubjectsEndpoints.Problem(422, "validation_failed", officeError);
        professor.Update(name, email, office, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return SubjectsEndpoints.Problem(412, "etag_mismatch", "O professor foi alterado; recarregue antes de tentar novamente."); }
        request.HttpContext.Response.Headers.ETag = SubjectsEndpoints.Etag(professor.Version);
        return Results.Ok(ToResponse(professor));
    }

    private static async Task<IResult> ArchiveAsync(HttpRequest request, Guid id, ArchiveProfessorRequest input, AcademicDbContext db,
        IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var professor = await db.Professors.SingleOrDefaultAsync(x => x.OwnerUserId == auth.UserId && x.Id == id, ct);
        if (professor is null) return Results.NotFound();
        if (!SubjectsEndpoints.TryIfMatch(request, professor.Version, out var precondition)) return precondition!;
        professor.SetArchived(input.Archived, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return SubjectsEndpoints.Problem(412, "etag_mismatch", "O professor foi alterado; recarregue antes de tentar novamente."); }
        request.HttpContext.Response.Headers.ETag = SubjectsEndpoints.Etag(professor.Version);
        return Results.Ok(ToResponse(professor));
    }

    private static bool IsValidEmail(string email) => MailAddress.TryCreate(email, out var parsed) &&
        string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);
    private static async Task<IdempotencyRecord?> FindIdempotencyAsync(AcademicDbContext db, Guid ownerId, string key, CancellationToken ct) =>
        await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == ownerId && x.Operation == CreateOperation && x.Key == key, ct);
    private static IResult Replay(IdempotencyRecord record, HttpResponse response)
    {
        if (record.Status != "completed" || record.ResponseJson is null) return InProgress();
        response.Headers.ETag = record.ETag;
        response.Headers.Location = record.Location;
        return Results.Content(record.ResponseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }
    private static IResult KeyReused() => SubjectsEndpoints.Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
    private static IResult InProgress() => SubjectsEndpoints.Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
    private static ProfessorResponse ToResponse(Professor x) => new(x.Id, x.Name, x.Email, x.Office, x.Archived, x.Version, x.CreatedAt, x.UpdatedAt);
    private static string Encode(ProfessorCursor value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    private static bool TryDecode(string encoded, out ProfessorCursor? value) { try { value = JsonSerializer.Deserialize<ProfessorCursor>(Convert.FromBase64String(encoded), JsonOptions); return value is not null && value.Id != Guid.Empty; } catch { value = null; return false; } }
    private sealed record ProfessorCursor(string Name, Guid Id);
    private sealed record NormalizedCreateProfessor(string Name, string? Email, string? Office);
}

public sealed record CreateProfessorRequest(string Name, string? Email = null, string? Office = null);
public sealed record ArchiveProfessorRequest(bool Archived);
public sealed record ProfessorResponse(Guid Id, string Name, string? Email, string? Office, bool Archived, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ProfessorListResponse(IReadOnlyList<ProfessorResponse> Items, string? NextCursor);
