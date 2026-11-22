using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace Nomi.Academic.Infrastructure.Identity;

public enum IdentityResolutionStatus { Authenticated, Unauthorized, Unavailable }
public sealed record IdentityResolution(IdentityResolutionStatus Status, Guid? UserId = null, string? Email = null, string? Name = null);
public interface IIdentityUserResolver
{
    Task<IdentityResolution> ResolveAsync(string? authorizationHeader, CancellationToken cancellationToken);
}

public sealed class IdentityUserResolver(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment) : IIdentityUserResolver
{
    public async Task<IdentityResolution> ResolveAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization) ||
            !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
            return new(IdentityResolutionStatus.Unauthorized);
        var baseUrl = configuration["Identity:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var identityBase) ||
            !string.IsNullOrEmpty(identityBase.UserInfo) || !string.IsNullOrEmpty(identityBase.Query) || !string.IsNullOrEmpty(identityBase.Fragment))
            return new(IdentityResolutionStatus.Unavailable);
        if (!string.Equals(identityBase.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !(environment.IsDevelopment() && identityBase.IsLoopback && string.Equals(identityBase.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            return new(IdentityResolutionStatus.Unavailable);
        var client = clients.CreateClient("identity");
        client.BaseAddress = new Uri(identityBase, identityBase.AbsolutePath.EndsWith('/') ? identityBase.AbsolutePath : identityBase.AbsolutePath + "/");
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization.Parameter);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new(IdentityResolutionStatus.Unauthorized);
            if (!response.IsSuccessStatusCode) return new(IdentityResolutionStatus.Unavailable);
            var user = await response.Content.ReadFromJsonAsync<IdentityUserResponse>(cancellationToken);
            return user is { Id: var id } && id != Guid.Empty ? new(IdentityResolutionStatus.Authenticated, id, user.Email, user.Name) : new(IdentityResolutionStatus.Unavailable);
        }
        catch (HttpRequestException) { return new(IdentityResolutionStatus.Unavailable); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(IdentityResolutionStatus.Unavailable); }
    }
    private sealed record IdentityUserResponse([property: JsonPropertyName("id")] Guid Id, string? Email, string? Name);
}
