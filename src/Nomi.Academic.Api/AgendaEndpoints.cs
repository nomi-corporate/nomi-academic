using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nomi.Academic.Domain;
using Nomi.Academic.Infrastructure.Identity;
using Nomi.Academic.Infrastructure.Persistence;

namespace Nomi.Academic.Api;

public static class AgendaEndpoints
{
    private const string CreateOperation = "commitments.create";
    private const string CreatedEventType = "academic.commitment.created.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/commitments", CreateAsync);
        endpoints.MapGet("/api/v1/agenda", AgendaAsync);
        endpoints.MapGet("/api/v1/home", HomeAsync);
        endpoints.MapGet("/api/v1/commitments/{id:guid}", GetAsync);
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
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(x => x.Name is not ("title" or "description" or "kind" or "subjectId" or "due")))
            return SubjectsEndpoints.Problem(400, "invalid_body", "Campos desconhecidos no compromisso.");
        var title = input.TryGetProperty("title", out var titleValue) && titleValue.ValueKind == JsonValueKind.String ? titleValue.GetString()?.Trim() : null;
        var kind = input.TryGetProperty("kind", out var kindValue) && kindValue.ValueKind == JsonValueKind.String ? kindValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160 || kind is not ("exam" or "assignment" or "reminder"))
            return SubjectsEndpoints.Problem(422, "validation_failed", "Informe título de até 160 caracteres e tipo válido.");
        string? description = null;
        if (input.TryGetProperty("description", out var descriptionValue))
        {
            if (descriptionValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                return SubjectsEndpoints.Problem(422, "validation_failed", "Descrição inválida.");
            description = descriptionValue.ValueKind == JsonValueKind.String ? descriptionValue.GetString()?.Trim() : null;
            if (description?.Length > 4000) return SubjectsEndpoints.Problem(422, "validation_failed", "Descrição muito longa.");
            if (description?.Length == 0) description = null;
        }
        Guid? subjectId = null;
        if (input.TryGetProperty("subjectId", out var subjectValue) && subjectValue.ValueKind != JsonValueKind.Null)
        {
            if (subjectValue.ValueKind != JsonValueKind.String || !Guid.TryParse(subjectValue.GetString(), out var parsedId) || parsedId == Guid.Empty)
                return SubjectsEndpoints.Problem(422, "validation_failed", "Matéria inválida.");
            subjectId = parsedId;
        }
        if (!input.TryGetProperty("due", out var dueValue) || dueValue.ValueKind != JsonValueKind.Object
            || dueValue.EnumerateObject().Any(x => x.Name is not ("precision" or "localDate" or "localTime" or "timeZone" or "offset")))
            return SubjectsEndpoints.Problem(422, "validation_failed", "Prazo inválido.");
        var precision = dueValue.TryGetProperty("precision", out var precisionValue) && precisionValue.ValueKind == JsonValueKind.String ? precisionValue.GetString() : null;
        var dateText = dueValue.TryGetProperty("localDate", out var dateValue) && dateValue.ValueKind == JsonValueKind.String ? dateValue.GetString() : null;
        var zoneText = dueValue.TryGetProperty("timeZone", out var zoneValue) && zoneValue.ValueKind == JsonValueKind.String ? zoneValue.GetString() : null;
        if (precision is not ("date" or "dateTime") || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || date.Year is < 2 or > 9998 || !ProfileEndpoints.IsIanaTimeZone(zoneText))
            return SubjectsEndpoints.Problem(422, "validation_failed", "Data ou fuso inválido.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneText!);
        DateTime? instant = null;
        string? localTime = null;
        string? offsetText = null;
        if (precision == "date")
        {
            if (dueValue.TryGetProperty("localTime", out _) || dueValue.TryGetProperty("offset", out _))
                return SubjectsEndpoints.Problem(422, "validation_failed", "Prazo sem horário não aceita hora ou offset.");
        }
        else
        {
            localTime = dueValue.TryGetProperty("localTime", out var timeValue) && timeValue.ValueKind == JsonValueKind.String ? timeValue.GetString() : null;
            offsetText = dueValue.TryGetProperty("offset", out var offsetValue) && offsetValue.ValueKind == JsonValueKind.String ? offsetValue.GetString() : null;
            if (!TimeOnly.TryParseExact(localTime, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                || offsetText is null || !System.Text.RegularExpressions.Regex.IsMatch(offsetText, @"^[+-](?:0\d|1[0-4]):[0-5]\d$")
                || !TimeSpan.TryParse(offsetText, CultureInfo.InvariantCulture, out var offset))
                return SubjectsEndpoints.Problem(422, "validation_failed", "Horário ou offset inválido.");
            var civil = date.ToDateTime(time, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(civil) || (zone.IsAmbiguousTime(civil)
                    ? !zone.GetAmbiguousTimeOffsets(civil).Contains(offset) : zone.GetUtcOffset(civil) != offset))
                return SubjectsEndpoints.Problem(422, "validation_failed", "Horário não corresponde ao fuso.");
            instant = new DateTimeOffset(civil, offset).UtcDateTime;
        }
        var owner = auth.UserId!.Value;
        var normalized = new { title, description, kind, subjectId, due = new { precision, localDate = date, localTime, timeZone = zone.Id, offset = offsetText } };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions)));
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.Operation == CreateOperation && x.Key == key, ct);
        if (existing is not null) return Replay(existing, hash, request.HttpContext.Response);
        string? subjectName = null;
        if (subjectId is { } requestedSubject)
        {
            subjectName = await db.Subjects.AsNoTracking().Where(x => x.Id == requestedSubject && x.OwnerUserId == owner && !x.Archived)
                .Select(x => x.Name).SingleOrDefaultAsync(ct);
            if (subjectName is null) return Results.NotFound();
        }
        var now = clock.GetUtcNow();
        var commitment = new Commitment(owner, title, kind!, subjectId, date, zone.Id, now, instant, description);
        var item = ToItem(commitment, zone, subjectName);
        var responseJson = JsonSerializer.Serialize(item, JsonOptions);
        var location = $"/api/v1/commitments/{commitment.Id}";
        var etag = SubjectsEndpoints.Etag(commitment.Version);
        var record = new IdempotencyRecord(owner, CreateOperation, key, hash, now, now.AddMinutes(2));
        record.Complete(responseJson, etag, location);
        var eventId = Guid.NewGuid();
        var eventJson = JsonSerializer.Serialize(new {
            eventId, eventType = CreatedEventType, schemaVersion = 1, occurredAt = now, producer = "academic",
            correlationId = request.HttpContext.TraceIdentifier, aggregateType = "commitment", aggregateId = commitment.Id,
            aggregateVersion = commitment.Version,
            data = new { userId = owner, commitmentId = commitment.Id, commitment.Title, commitment.Kind,
                commitment.Status, commitment.Deleted, item.Due }
        }, JsonOptions);
        db.Commitments.Add(commitment);
        db.IdempotencyRecords.Add(record);
        db.OutboxEvents.Add(new OutboxEvent(eventId, CreatedEventType, commitment.Id, commitment.Version, eventJson, now));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.Operation == CreateOperation && x.Key == key, ct);
            if (existing is null) throw;
            return Replay(existing, hash, request.HttpContext.Response);
        }
        request.HttpContext.Response.Headers.Location = location;
        request.HttpContext.Response.Headers.ETag = etag;
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Content(responseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }

    private static IResult Replay(IdempotencyRecord record, string hash, HttpResponse response)
    {
        if (record.RequestHash != hash) return SubjectsEndpoints.Problem(409, "idempotency_key_reused", "A chave já foi usada com outro conteúdo.");
        if (record.Status != "completed" || record.ResponseJson is null) return SubjectsEndpoints.Problem(409, "request_in_progress", "Solicitação em andamento.");
        response.Headers.Location = record.Location;
        response.Headers.ETag = record.ETag;
        response.Headers.CacheControl = "no-store";
        return Results.Content(record.ResponseJson, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
    }

    private static async Task<IResult> AgendaAsync(HttpRequest request, AcademicDbContext db, IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        var query = request.Query;
        if (!DateOnly.TryParseExact(query["fromDate"], "yyyy-MM-dd", out var from)
            || !DateOnly.TryParseExact(query["toDate"], "yyyy-MM-dd", out var to)
            || from.Year < 2 || to.Year > 9998 || to <= from || to.DayNumber - from.DayNumber > 31 || !ProfileEndpoints.IsIanaTimeZone(query["timeZone"]))
            return Results.Problem(statusCode: 422, title: "invalid_agenda_range");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(query["timeZone"]!);
        var fromInstant = StartOfDate(from, zone);
        var toInstant = StartOfDate(to, zone);
        var filter = $"{from:yyyy-MM-dd}|{to:yyyy-MM-dd}|{zone.Id}";
        var offset = 0;
        if (query.TryGetValue("cursor", out var cursor))
        {
            try
            {
                var decoded = JsonSerializer.Deserialize<AgendaCursor>(Convert.FromBase64String(cursor!));
                if (decoded is null || decoded.Filter != filter || decoded.Offset is < 0 or > 1000) throw new FormatException();
                offset = decoded.Offset;
            }
            catch (Exception error) when (error is FormatException or JsonException)
            { return Results.Problem(statusCode: 400, title: "invalid_cursor"); }
        }
        var limit = query.TryGetValue("limit", out var rawLimit) && int.TryParse(rawLimit, out var parsed) ? Math.Clamp(parsed, 1, 100) : 20;
        var matches = await Owned(db, auth.UserId!.Value)
            .Where(x => x.DueInstant == null ? x.LocalDate >= from && x.LocalDate < to : x.DueInstant >= fromInstant && x.DueInstant < toInstant)
            .Take(1001).ToListAsync(ct);
        if (matches.Count > 1000) return Results.Problem(statusCode: 422, title: "agenda_range_too_dense", detail: "Consulte um intervalo menor.");
        var items = Sort(matches.Select(x => ToItem(x, zone))).ToList();
        var page = items.Skip(offset).Take(limit).ToArray();
        var nextCursor = offset + page.Length < items.Count ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new AgendaCursor(filter, offset + page.Length))) : null;
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { items = page, nextCursor, fromDate = from, toDate = to, timeZone = zone.Id, generatedAt = clock.GetUtcNow(), classOccurrencesAvailable = false });
    }

    private static async Task<IResult> HomeAsync(HttpRequest request, AcademicDbContext db, IIdentityUserResolver identity, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        if (!DateOnly.TryParseExact(request.Query["date"], "yyyy-MM-dd", out var date) || date.Year is < 2 or > 9998 || !ProfileEndpoints.IsIanaTimeZone(request.Query["timeZone"]))
            return Results.Problem(statusCode: 422, title: "invalid_home_date");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.Query["timeZone"]!);
        var now = clock.GetUtcNow();
        var start = StartOfDate(date, zone);
        var end = StartOfDate(date.AddDays(1), zone);
        var query = Owned(db, auth.UserId!.Value);
        var today = await query.Where(x => x.DueInstant == null ? x.LocalDate == date : x.DueInstant >= start && x.DueInstant < end).Take(1001).ToListAsync(ct);
        if (today.Count > 1000) return Results.Problem(statusCode: 422, title: "home_date_too_dense");
        // Read each precision independently so their ordering cannot hide the earliest deadline.
        var pending = query.Where(x => x.Status == "pending");
        var futureTimed = await pending.Where(x => x.DueInstant >= now.UtcDateTime).OrderBy(x => x.DueInstant).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        var pastTimed = await pending.Where(x => x.DueInstant < now.UtcDateTime).OrderBy(x => x.DueInstant).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        var civilToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var lower = civilToday.AddDays(-2);
        var upper = civilToday.AddDays(2);
        var dateBoundary = await pending.Where(x => x.DueInstant == null && x.LocalDate >= lower && x.LocalDate < upper).Take(1001).ToListAsync(ct);
        if (dateBoundary.Count > 1000) return Results.Problem(statusCode: 422, title: "home_date_too_dense");
        var dateFuture = await pending.Where(x => x.DueInstant == null && x.LocalDate >= upper).OrderBy(x => x.LocalDate).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        var datePast = await pending.Where(x => x.DueInstant == null && x.LocalDate < lower).OrderBy(x => x.LocalDate).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        var upcoming = Sort(futureTimed.Concat(dateFuture).Concat(dateBoundary.Where(x => !IsOverdue(x, now))).Select(x => ToItem(x, zone))).ToList();
        var overdue = Sort(pastTimed.Concat(datePast).Concat(dateBoundary.Where(x => IsOverdue(x, now))).Select(x => ToItem(x, zone))).ToList();
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new {
            date, timeZone = zone.Id, generatedAt = now,
            items = Sort(today.Select(x => ToItem(x, zone))).Take(100).ToArray(),
            upcoming = upcoming.Take(100).ToArray(), overdue = overdue.Take(100).ToArray(),
            hasMore = today.Count > 100 || upcoming.Count > 100 || overdue.Count > 100,
            classOccurrencesAvailable = false
        });
    }

    private static async Task<IResult> GetAsync(HttpRequest request, Guid id, AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        var commitment = await Owned(db, auth.UserId!.Value).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (commitment is null) return Results.NotFound();
        var requestedZone = request.Query["timeZone"].ToString();
        if (requestedZone.Length > 0 && !ProfileEndpoints.IsIanaTimeZone(requestedZone))
            return Results.Problem(statusCode: 422, title: "invalid_time_zone");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(requestedZone.Length > 0 ? requestedZone : commitment.TimeZone);
        request.HttpContext.Response.Headers.ETag = $"\"{commitment.Version}\"";
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(ToItem(commitment, zone));
    }

    private static IQueryable<Commitment> Owned(AcademicDbContext db, Guid owner) => db.Commitments.AsNoTracking().Include(x => x.Subject).Where(x => x.OwnerUserId == owner && !x.Deleted);
    private static IEnumerable<AgendaItem> Sort(IEnumerable<AgendaItem> values) => values.OrderBy(x => x.DisplayDate).ThenBy(x => x.DisplayTime, StringComparer.Ordinal).ThenBy(x => x.Id);
    private static DateTime StartOfDate(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some IANA dates begin after midnight because of historical DST changes.
        for (var minute = 0; zone.IsInvalidTime(local) && minute < 24 * 60; minute++) local = local.AddMinutes(1);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("Invalid civil date.");
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).UtcDateTime;
    }
    private static bool IsOverdue(Commitment value, DateTimeOffset now) => value.DueInstant is { } instant ? instant < now.UtcDateTime
        : value.LocalDate < DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(value.TimeZone)).DateTime);
    private static AgendaItem ToItem(Commitment value, TimeZoneInfo zone, string? subjectName = null)
    {
        var local = value.DueInstant is { } instant ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc), zone) : (DateTime?)null;
        var original = value.DueInstant is { } utc ? TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), TimeZoneInfo.FindSystemTimeZoneById(value.TimeZone)) : (DateTimeOffset?)null;
        return new(value.Id, "commitment", value.Title, value.Description, value.Kind, value.SubjectId, subjectName ?? value.Subject?.Name, value.Status,
            local is { } date ? DateOnly.FromDateTime(date) : value.LocalDate,
            local?.ToString("HH:mm", CultureInfo.InvariantCulture), new DueResponse(value.DueInstant is null ? "date" : "dateTime", value.LocalDate, value.TimeZone,
                original?.UtcDateTime, original?.ToString("HH:mm:ss", CultureInfo.InvariantCulture), original?.ToString("zzz", CultureInfo.InvariantCulture)),
            value.Version, value.CreatedAt, value.UpdatedAt);
    }
    private sealed record AgendaCursor(string Filter, int Offset);
}

public sealed record DueResponse(string Precision, DateOnly LocalDate, string TimeZone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? Instant,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocalTime,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Offset);
public sealed record AgendaItem(Guid Id, string ItemType, string Title, string? Description, string Kind, Guid? SubjectId, string? SubjectName,
    string Status, DateOnly DisplayDate, string? DisplayTime, DueResponse Due, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
