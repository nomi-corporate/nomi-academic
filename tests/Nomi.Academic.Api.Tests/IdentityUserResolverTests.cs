using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Nomi.Academic.Infrastructure.Identity;

namespace Nomi.Academic.Api.Tests;

public sealed class IdentityUserResolverTests
{
    [Fact]
    public async Task InvalidBearerIsRejectedWithoutCallingIdentity()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var resolver = CreateResolver("https://identity.example", Environments.Production, handler);
        var result = await resolver.ResolveAsync("Basic not-a-bearer", CancellationToken.None);
        Assert.Equal(IdentityResolutionStatus.Unauthorized, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task IdentityFailureDoesNotAuthenticateOrProduceAUserId()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var resolver = CreateResolver("https://identity.example", Environments.Production, handler);
        var result = await resolver.ResolveAsync("Bearer opaque-token", CancellationToken.None);
        Assert.Equal(IdentityResolutionStatus.Unavailable, result.Status);
        Assert.Null(result.UserId);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("Production", "http://identity.internal:5104")]
    [InlineData("Production", "http://localhost:5104")]
    [InlineData("Development", "http://10.0.2.2:5104")]
    [InlineData("Development", "http://localhost:5104?target=other")]
    public async Task InsecureOrMalformedIdentityOriginFailsClosed(string environment, string baseUrl)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var resolver = CreateResolver(baseUrl, environment, handler);
        var result = await resolver.ResolveAsync("Bearer opaque-token", CancellationToken.None);
        Assert.Equal(IdentityResolutionStatus.Unavailable, result.Status);
        Assert.Null(result.UserId);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DevelopmentAllowsLoopbackIdentityAndReadsItsValidatedUserId()
    {
        var expected = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = expected })
        });
        var resolver = CreateResolver("http://localhost:5104", Environments.Development, handler);
        var result = await resolver.ResolveAsync("Bearer opaque-token", CancellationToken.None);
        Assert.Equal(IdentityResolutionStatus.Authenticated, result.Status);
        Assert.Equal(expected, result.UserId);
        Assert.Equal(1, handler.RequestCount);
    }

    private static IdentityUserResolver CreateResolver(string baseUrl, string environment, StubHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:BaseUrl"] = baseUrl
        }).Build();
        var hostEnvironment = new StubHostEnvironment(environment);
        return new IdentityUserResolver(new StubHttpClientFactory(handler), configuration, hostEnvironment);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Nomi.Academic.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
