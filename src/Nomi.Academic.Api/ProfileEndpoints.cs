using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nomi.Academic.Domain;
using Nomi.Academic.Infrastructure.Identity;
using Nomi.Academic.Infrastructure.Persistence;

namespace Nomi.Academic.Api;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me", GetAsync);
        endpoints.MapPatch("/api/v1/me", PatchAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(HttpRequest request, AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        var profile = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == auth.UserId, ct);
        if (profile is null)
        {
            profile = new UserProfile(auth.UserId!.Value, auth.Name);
            db.UserProfiles.Add(profile);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Concurrent first reads may initialize the same authenticated account.
                db.ChangeTracker.Clear();
                profile = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == auth.UserId, ct);
                if (profile is null) throw;
            }
        }
        return Respond(request.HttpContext.Response, profile, auth.Email);
    }

    private static async Task<IResult> PatchAsync(HttpRequest request, JsonElement input, AcademicDbContext db, IIdentityUserResolver identity, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated) return Results.StatusCode(503);
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(p => p.Name is not ("displayName" or "theme" or "locale" or "timeZone")))
            return Results.Problem(statusCode: 400, title: "invalid_profile_fields");
        var profile = await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == auth.UserId, ct);
        if (profile is null) return Results.NotFound();
        if (!SubjectsEndpoints.TryIfMatch(request, profile.Version, out var precondition)) return precondition!;
        var name = Read(input, "displayName", profile.DisplayName);
        var theme = Read(input, "theme", profile.Theme);
        var locale = Read(input, "locale", profile.Locale);
        var timeZone = Read(input, "timeZone", profile.TimeZone);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || theme is not ("system" or "light" or "dark") || locale != "pt-BR" || !IsIanaTimeZone(timeZone))
            return Results.Problem(statusCode: 422, title: "invalid_profile");
        profile.Update(name, theme, locale, timeZone!);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 412, title: "etag_mismatch"); }
        return Respond(request.HttpContext.Response, profile, auth.Email);
    }

    internal static bool IsIanaTimeZone(string? value) => value is { Length: > 0 and <= 100 }
        && (value == "UTC" || value.Contains('/')) && TimeZoneInfo.TryFindSystemTimeZoneById(value, out _);
    private static string? Read(JsonElement input, string name, string current) => input.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null : current;
    private static IResult Respond(HttpResponse response, UserProfile profile, string? email)
    {
        response.Headers.ETag = $"\"{profile.Version}\"";
        response.Headers.CacheControl = "no-store";
        return Results.Ok(new { id = profile.UserId, profile.DisplayName, email, profile.Theme, profile.Locale, profile.TimeZone, profile.Version, avatarUrl = (string?)null });
    }
}
