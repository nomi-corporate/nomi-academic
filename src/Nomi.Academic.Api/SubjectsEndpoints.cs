using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nomi.Academic.Domain;
using Nomi.Academic.Infrastructure.Identity;
using Nomi.Academic.Infrastructure.Persistence;

namespace Nomi.Academic.Api;

public static class SubjectsEndpoints
{
    private const string CreateOperation = "subjects.create";
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan IdempotencyLease = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSubjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/subjects");
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", PatchAsync);
        group.MapPut("/{id:guid}/archive", ArchiveAsync);
        group.MapPut("/{id:guid}/professors", ReplaceProfessorsAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, HttpResponse response, CreateSubjectRequest input, AcademicDbContext db,
        IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var idempotency) || string.IsNullOrWhiteSpace(idempotency))
            return Problem(400, "idempotency_key_required", "Idempotency-Key é obrigatório para criar uma matéria.");
        var key = idempotency.ToString().Trim();
        if (key.Length > 128) return Problem(400, "invalid_idempotency_key", "Idempotency-Key deve ter no máximo 128 caracteres.");

        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        if (!ValidateName(input.Name, out var nameError)) return Problem(422, "validation_failed", nameError!);
        if (!ValidateLength(input.Code, 40, "code", out var codeError)) return Problem(422, "validation_failed", codeError!);
        if (!ValidateLength(input.ColorToken, 80, "colorToken", out var colorError)) return Problem(422, "validation_failed", colorError!);
        if (!ValidateLength(input.IconKey, 80, "iconKey", out var iconError)) return Problem(422, "validation_failed", iconError!);

        var ownerId = auth.UserId!.Value;
        var professorIds = NormalizeIds(input.ProfessorIds, out var duplicateIds);
        if (duplicateIds) return Problem(422, "validation_failed", "professorIds não pode conter IDs duplicados.");
        var requestValue = new NormalizedCreateSubject(input.Name.Trim(), Normalize(input.Code), Normalize(input.ColorToken), Normalize(input.IconKey), professorIds);
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(requestValue, JsonOptions)));

        var now = clock.GetUtcNow();
        await db.IdempotencyRecords.Where(x => x.CreatedAt <= now - IdempotencyRetention).ExecuteDeleteAsync(ct);
        var existing = await FindIdempotencyAsync(db, ownerId, key, ct);
        if (existing is not null)
        {
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                    return Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
                if (existing.Status == "completed") return Replay(existing, requestHash, response);
                if (existing.LeaseExpiresAt > now) return Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
                var oldLease = existing.LeaseExpiresAt;
                var reclaimed = await db.IdempotencyRecords.Where(x => x.OwnerUserId == ownerId && x.Operation == CreateOperation && x.Key == key && x.Status == "pending" && x.LeaseExpiresAt == oldLease)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseExpiresAt, now + IdempotencyLease), ct);
                if (reclaimed == 0) return Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
                existing = await FindIdempotencyAsync(db, ownerId, key, ct);
            }
        }

        if (existing is not null && !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            return Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
        if (existing?.Status == "completed") return Replay(existing, requestHash, response);

        if (professorIds.Length > 0)
        {
            var validProfessorIds = await db.Professors.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerId && !x.Archived && professorIds.Contains(x.Id))
                .Select(x => x.Id).ToListAsync(ct);
            if (validProfessorIds.Count != professorIds.Length) return Results.NotFound();
        }

        if (existing is not null)
        {
            if (existing.LeaseExpiresAt > now) return Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
            var oldLease = existing.LeaseExpiresAt;
            var reclaimed = await db.IdempotencyRecords.Where(x => x.OwnerUserId == ownerId && x.Operation == CreateOperation && x.Key == key && x.Status == "pending" && x.LeaseExpiresAt == oldLease)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseExpiresAt, now + IdempotencyLease), ct);
            if (reclaimed == 0) return Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
            existing = await FindIdempotencyAsync(db, ownerId, key, ct);
        }
        else
        {
            var reservation = new IdempotencyRecord(ownerId, CreateOperation, key, requestHash, now, now + IdempotencyLease);
            db.IdempotencyRecords.Add(reservation);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                existing = await FindIdempotencyAsync(db, ownerId, key, ct);
                if (existing is null) throw;
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                    return Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
                if (existing.Status == "completed") return Replay(existing, requestHash, response);
                return Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
            }
        }

        var subject = new Subject(ownerId, requestValue.Name, requestValue.Code, requestValue.ColorToken, requestValue.IconKey, now);
        foreach (var professorId in professorIds)
            db.SubjectProfessors.Add(new SubjectProfessor(ownerId, subject.Id, professorId));
        var subjectResponse = new SubjectResponse(subject.Id, subject.Name, subject.Code, subject.ColorToken,
            subject.IconKey, professorIds, subject.Archived, subject.Version, subject.CreatedAt, subject.UpdatedAt);
        var responseJson = JsonSerializer.Serialize(subjectResponse, JsonOptions);
        var etag = Etag(subject.Version);
        var location = $"/api/v1/subjects/{subject.Id}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Subjects.Add(subject);
        var idempotencyRecord = await db.IdempotencyRecords.SingleAsync(x => x.OwnerUserId == ownerId && x.Operation == CreateOperation && x.Key == key, ct);
        idempotencyRecord.Complete(responseJson, etag, location);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            existing = await FindIdempotencyAsync(db, ownerId, key, ct);
            if (existing is null) throw;
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) return Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
            return existing.Status == "completed"
                ? Replay(existing, requestHash, response)
                : Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
        }

        response.Headers.ETag = etag;
        response.Headers.Location = location;
        return Results.Content(responseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request, bool? archived, string? query, string? cursor, int? limit,
        AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var pageSize = Math.Clamp(limit ?? 50, 1, 100);
        SubjectCursor? after = null;
        if (!string.IsNullOrWhiteSpace(cursor) && !TryDecode(cursor, out after)) return Problem(400, "invalid_cursor", "O cursor informado é inválido.");

        var includeArchived = archived ?? false;
        var subjects = db.Subjects.AsNoTracking().Where(x => x.OwnerUserId == auth.UserId && x.Archived == includeArchived);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var normalizedTerm = term.ToLowerInvariant();
            subjects = subjects.Where(x => x.Name.ToLower().Contains(normalizedTerm) || (x.Code != null && x.Code.ToLower().Contains(normalizedTerm)));
        }
        if (after is not null)
            subjects = subjects.Where(x => string.Compare(x.Name, after.Name) > 0 || (x.Name == after.Name && x.Id.CompareTo(after.Id) > 0));
        var page = await subjects.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(pageSize + 1).ToListAsync(ct);
        var hasMore = page.Count > pageSize;
        if (hasMore) page.RemoveAt(pageSize);
        var subjectIds = page.Select(x => x.Id).ToArray();
        var links = await db.SubjectProfessors.AsNoTracking()
            .Where(x => x.OwnerUserId == auth.UserId && subjectIds.Contains(x.SubjectId))
            .Select(x => new { x.SubjectId, x.ProfessorId }).ToListAsync(ct);
        var professorIds = links.GroupBy(x => x.SubjectId).ToDictionary(x => x.Key, x => (IReadOnlyList<Guid>)x.Select(link => link.ProfessorId).Order().ToArray());
        var next = hasMore ? Encode(new(page[^1].Name, page[^1].Id)) : null;
        return Results.Ok(new SubjectListResponse(page.Select(x => ToResponse(x, professorIds.GetValueOrDefault(x.Id, Array.Empty<Guid>()))).ToArray(), next));
    }

    private static async Task<IResult> GetAsync(HttpRequest request, Guid id, AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var subject = await db.Subjects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == auth.UserId, ct);
        if (subject is null) return Results.NotFound();
        var professorIds = await db.SubjectProfessors.AsNoTracking().Where(x => x.OwnerUserId == auth.UserId && x.SubjectId == id)
            .Select(x => x.ProfessorId).Order().ToArrayAsync(ct);
        request.HttpContext.Response.Headers.ETag = Etag(subject.Version);
        return Results.Ok(ToResponse(subject, professorIds));
    }

    private static async Task<IResult> ReplaceProfessorsAsync(HttpRequest request, Guid id, ReplaceSubjectProfessorsRequest input,
        AcademicDbContext db, IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var subject = await db.Subjects.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == auth.UserId, ct);
        if (subject is null) return Results.NotFound();
        if (!TryIfMatch(request, subject.Version, out var precondition)) return precondition!;
        if (input.ProfessorIds is null) return Problem(422, "validation_failed", "professorIds é obrigatório.");
        var professorIds = input.ProfessorIds.Distinct().Order().ToArray();
        if (professorIds.Length != input.ProfessorIds.Count) return Problem(422, "validation_failed", "professorIds não pode conter IDs duplicados.");
        if (professorIds.Any(x => x == Guid.Empty)) return Problem(422, "validation_failed", "professorIds contém um ID inválido.");
        if (professorIds.Length > 0)
        {
            var validProfessorIds = await db.Professors.AsNoTracking()
                .Where(x => x.OwnerUserId == auth.UserId && !x.Archived && professorIds.Contains(x.Id))
                .Select(x => x.Id).ToListAsync(ct);
            if (validProfessorIds.Count != professorIds.Length) return Results.NotFound();
        }
        var oldLinks = await db.SubjectProfessors.Where(x => x.OwnerUserId == auth.UserId && x.SubjectId == id).ToListAsync(ct);
        if (oldLinks.Select(x => x.ProfessorId).Order().SequenceEqual(professorIds))
        {
            request.HttpContext.Response.Headers.ETag = Etag(subject.Version);
            return Results.Ok(ToResponse(subject, professorIds));
        }
        db.SubjectProfessors.RemoveRange(oldLinks);
        foreach (var professorId in professorIds) db.SubjectProfessors.Add(new SubjectProfessor(auth.UserId!.Value, id, professorId));
        subject.MarkProfessorLinksChanged(clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Problem(412, "etag_mismatch", "A matéria foi alterada; recarregue antes de tentar novamente."); }
        request.HttpContext.Response.Headers.ETag = Etag(subject.Version);
        return Results.Ok(ToResponse(subject, professorIds));
    }

    private static async Task<IResult> PatchAsync(HttpRequest request, Guid id, JsonElement input, AcademicDbContext db, IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        if (input.ValueKind != JsonValueKind.Object) return Problem(400, "invalid_body", "O corpo precisa ser um objeto JSON.");
        var allowed = new[] { "name", "code", "colorToken", "iconKey" };
        if (input.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal))) return Problem(400, "unknown_field", "O corpo contém campo não permitido.");
        var subject = await db.Subjects.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == auth.UserId, ct);
        if (subject is null) return Results.NotFound();
        if (!TryIfMatch(request, subject.Version, out var precondition)) return precondition!;
        var name = subject.Name;
        if (input.TryGetProperty("name", out var nameElement))
        {
            if (nameElement.ValueKind != JsonValueKind.String) return Problem(400, "invalid_name", "Nome inválido.");
            if (!ValidateName(nameElement.GetString(), out var error)) return Problem(422, "validation_failed", error!);
            name = nameElement.GetString()!.Trim();
        }
        var code = ReadNullableString(input, "code", subject.Code, 40, out var codeError);
        if (codeError is not null) return Problem(422, "validation_failed", codeError);
        var color = ReadNullableString(input, "colorToken", subject.ColorToken, 80, out var colorError);
        if (colorError is not null) return Problem(422, "validation_failed", colorError);
        var icon = ReadNullableString(input, "iconKey", subject.IconKey, 80, out var iconError);
        if (iconError is not null) return Problem(422, "validation_failed", iconError);
        subject.Update(name, code, color, icon, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Problem(412, "etag_mismatch", "A matéria foi alterada; recarregue antes de tentar novamente."); }
        request.HttpContext.Response.Headers.ETag = Etag(subject.Version);
        var professorIds = await db.SubjectProfessors.AsNoTracking().Where(x => x.OwnerUserId == auth.UserId && x.SubjectId == id).Select(x => x.ProfessorId).Order().ToArrayAsync(ct);
        return Results.Ok(ToResponse(subject, professorIds));
    }

    private static async Task<IResult> ArchiveAsync(HttpRequest request, Guid id, ArchiveSubjectRequest input, AcademicDbContext db, IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status == IdentityResolutionStatus.Unavailable) return Results.StatusCode(503);
        var subject = await db.Subjects.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == auth.UserId, ct);
        if (subject is null) return Results.NotFound();
        if (!TryIfMatch(request, subject.Version, out var precondition)) return precondition!;
        subject.SetArchived(input.Archived, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Problem(412, "etag_mismatch", "A matéria foi alterada; recarregue antes de tentar novamente."); }
        request.HttpContext.Response.Headers.ETag = Etag(subject.Version);
        var professorIds = await db.SubjectProfessors.AsNoTracking().Where(x => x.OwnerUserId == auth.UserId && x.SubjectId == id).Select(x => x.ProfessorId).Order().ToArrayAsync(ct);
        return Results.Ok(ToResponse(subject, professorIds));
    }

    internal static bool ValidateName(string? value, out string? error) { error = string.IsNullOrWhiteSpace(value) ? "O nome é obrigatório." : value.Trim().Length > 120 ? "O nome deve ter no máximo 120 caracteres." : null; return error is null; }
    internal static bool ValidateLength(string? value, int max, string field, out string? error) { error = value is not null && value.Trim().Length > max ? $"{field} deve ter no máximo {max} caracteres." : null; return error is null; }
    internal static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static string? ReadNullableString(JsonElement input, string name, string? current, int max, out string? error)
    {
        error = null;
        if (!input.TryGetProperty(name, out var element)) return current;
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String) { error = $"{name} precisa ser texto ou nulo."; return current; }
        var value = element.GetString()!.Trim();
        if (value.Length > max) { error = $"{name} deve ter no máximo {max} caracteres."; return current; }
        return value.Length == 0 ? null : value;
    }
    internal static IResult Problem(int status, string code, string detail) => Results.Problem(statusCode: status, title: code, detail: detail);
    internal static string Etag(int version) => $"\"{version}\"";
    internal static bool TryIfMatch(HttpRequest request, int version, out IResult? result)
    {
        result = null;
        if (!request.Headers.TryGetValue("If-Match", out var value) || string.IsNullOrWhiteSpace(value))
        { result = Problem(428, "if_match_required", "If-Match é obrigatório para alterar o recurso."); return false; }
        if (!string.Equals(value.ToString().Trim(), Etag(version), StringComparison.Ordinal))
        { result = Problem(412, "etag_mismatch", "O recurso foi alterado; recarregue antes de tentar novamente."); return false; }
        return true;
    }
    internal static SubjectResponse ToResponse(Subject subject, IReadOnlyList<Guid> professorIds) => new(subject.Id, subject.Name, subject.Code, subject.ColorToken, subject.IconKey, professorIds, subject.Archived, subject.Version, subject.CreatedAt, subject.UpdatedAt);
    private static Guid[] NormalizeIds(IReadOnlyList<Guid>? ids, out bool duplicates)
    {
        var values = (ids ?? Array.Empty<Guid>()).ToArray();
        duplicates = values.Distinct().Count() != values.Length;
        return values.Distinct().Order().ToArray();
    }
    private static async Task<IdempotencyRecord?> FindIdempotencyAsync(AcademicDbContext db, Guid ownerId, string key, CancellationToken ct) =>
        await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == ownerId && x.Operation == CreateOperation && x.Key == key, ct);
    private static IResult Replay(IdempotencyRecord record, string requestHash, HttpResponse response)
    {
        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            return Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
        if (record.Status != "completed" || record.ResponseJson is null) return Problem(409, "request_in_progress", "A solicitação ainda está sendo processada. Tente novamente em instantes.");
        response.Headers.ETag = record.ETag;
        response.Headers.Location = record.Location;
        return Results.Content(record.ResponseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }
    private static string Encode(SubjectCursor value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
    private static bool TryDecode(string encoded, out SubjectCursor? value) { try { value = JsonSerializer.Deserialize<SubjectCursor>(Convert.FromBase64String(encoded), JsonOptions); return value is not null && value.Id != Guid.Empty; } catch { value = null; return false; } }
    private sealed record NormalizedCreateSubject(string Name, string? Code, string? ColorToken, string? IconKey, Guid[] ProfessorIds);
    private sealed record SubjectCursor(string Name, Guid Id);
}

public sealed record CreateSubjectRequest(string Name, string? Code = null, string? ColorToken = null, string? IconKey = null, IReadOnlyList<Guid>? ProfessorIds = null);
public sealed record ArchiveSubjectRequest(bool Archived);
public sealed record ReplaceSubjectProfessorsRequest(IReadOnlyList<Guid>? ProfessorIds);
public sealed record SubjectResponse(Guid Id, string Name, string? Code, string? ColorToken, string? IconKey, IReadOnlyList<Guid> ProfessorIds, bool Archived, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SubjectListResponse(IReadOnlyList<SubjectResponse> Items, string? NextCursor);
