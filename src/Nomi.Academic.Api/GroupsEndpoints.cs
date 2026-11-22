using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nomi.Academic.Domain;
using Nomi.Academic.Infrastructure.Identity;
using Nomi.Academic.Infrastructure.Persistence;

namespace Nomi.Academic.Api;

public static class GroupsEndpoints
{
    private const string CreateOperation = "groups.create";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapGroupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/groups");
        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, JsonElement input, AcademicDbContext db,
        IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        if (!request.Headers.TryGetValue("Idempotency-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
            return SubjectsEndpoints.Problem(400, "idempotency_key_required", "Idempotency-Key é obrigatório.");
        var key = rawKey.ToString().Trim();
        if (key.Length > 128) return SubjectsEndpoints.Problem(400, "invalid_idempotency_key", "Chave de idempotência inválida.");
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(x => x.Name is not ("name" or "description")))
            return SubjectsEndpoints.Problem(400, "invalid_body", "Informe apenas nome e descrição.");
        var name = input.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return SubjectsEndpoints.Problem(422, "validation_failed", "Nome do grupo deve ter de 1 a 120 caracteres.");
        string? description = null;
        if (input.TryGetProperty("description", out var descriptionValue))
        {
            if (descriptionValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                return SubjectsEndpoints.Problem(422, "validation_failed", "Descrição inválida.");
            description = descriptionValue.ValueKind == JsonValueKind.String ? descriptionValue.GetString()?.Trim() : null;
            if (description?.Length > 2000) return SubjectsEndpoints.Problem(422, "validation_failed", "Descrição deve ter no máximo 2000 caracteres.");
            if (description?.Length == 0) description = null;
        }
        var owner = auth.UserId!.Value;
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { name, description }, JsonOptions)));
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.Operation == CreateOperation && x.Key == key, ct);
        if (existing is not null) return Replay(existing, hash, request.HttpContext.Response);
        var now = clock.GetUtcNow();
        var studyGroup = new StudyGroup(owner, name, description, now);
        var response = new GroupResponse(studyGroup.Id, studyGroup.Name, studyGroup.Description, "owner", studyGroup.Version, studyGroup.CreatedAt, studyGroup.UpdatedAt);
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var location = $"/api/v1/groups/{studyGroup.Id}";
        var record = new IdempotencyRecord(owner, CreateOperation, key, hash, now, now.AddMinutes(2));
        record.Complete(json, SubjectsEndpoints.Etag(studyGroup.Version), location);
        db.StudyGroups.Add(studyGroup);
        db.StudyGroupMemberships.Add(new StudyGroupMembership(studyGroup.Id, owner, "owner"));
        db.IdempotencyRecords.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.Operation == CreateOperation && x.Key == key, ct);
            if (existing is null) throw;
            return Replay(existing, hash, request.HttpContext.Response);
        }
        request.HttpContext.Response.Headers.Location = location;
        request.HttpContext.Response.Headers.ETag = SubjectsEndpoints.Etag(studyGroup.Version);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Content(json, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListAsync(HttpRequest request, string? cursor, int? limit,
        AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        Guid? after = null;
        if (cursor is not null)
        {
            try { after = new Guid(Convert.FromBase64String(cursor)); }
            catch { return SubjectsEndpoints.Problem(400, "invalid_cursor", "Cursor inválido."); }
        }
        var size = Math.Clamp(limit ?? 20, 1, 100);
        var memberships = db.StudyGroupMemberships.AsNoTracking().Include(x => x.Group)
            .Where(x => x.UserId == auth.UserId && x.Active && x.Group.Active);
        if (after is { } id) memberships = memberships.Where(x => x.GroupId.CompareTo(id) > 0);
        var rows = await memberships.OrderBy(x => x.GroupId).Take(size + 1).ToListAsync(ct);
        var hasMore = rows.Count > size;
        if (hasMore) rows.RemoveAt(size);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { items = rows.Select(ToResponse).ToArray(), nextCursor = hasMore ? Convert.ToBase64String(rows[^1].GroupId.ToByteArray()) : null });
    }

    private static async Task<IResult> GetAsync(HttpRequest request, Guid id, AcademicDbContext db,
        IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        var member = await db.StudyGroupMemberships.AsNoTracking().Include(x => x.Group)
            .SingleOrDefaultAsync(x => x.GroupId == id && x.UserId == auth.UserId && x.Active && x.Group.Active, ct);
        if (member is null) return Results.NotFound();
        request.HttpContext.Response.Headers.ETag = SubjectsEndpoints.Etag(member.Group.Version);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(ToResponse(member));
    }

    private static GroupResponse ToResponse(StudyGroupMembership member) => new(member.Group.Id, member.Group.Name,
        member.Group.Description, member.Role, member.Group.Version, member.Group.CreatedAt, member.Group.UpdatedAt);
    private static IResult Replay(IdempotencyRecord record, string hash, HttpResponse response)
    {
        if (record.RequestHash != hash) return SubjectsEndpoints.Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
        if (record.Status != "completed" || record.ResponseJson is null) return SubjectsEndpoints.Problem(409, "request_in_progress", "Solicitação em andamento.");
        response.Headers.Location = record.Location;
        response.Headers.ETag = record.ETag;
        response.Headers.CacheControl = "no-store";
        return Results.Content(record.ResponseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }
    private sealed record GroupResponse(Guid Id, string Name, string? Description, string Role, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
