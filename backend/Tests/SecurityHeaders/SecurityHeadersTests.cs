using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Tests.SecurityHeaders;

public sealed class SecurityHeadersTests : IClassFixture<ApiFixture>
{
    private static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<string, string>
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'",
        ["X-Frame-Options"] = "DENY",
        ["Referrer-Policy"] = "no-referrer",
        ["Cross-Origin-Resource-Policy"] = "same-origin",
        ["Cache-Control"] = "no-store",
    };

    private readonly ApiFixture _fixture;

    public SecurityHeadersTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Success_CarriesTheSecurityHeaders()
    {
        SessionResponse response = await new TestSession(_fixture.CreateClient())
            .SendAsync(HttpMethod.Get, "api/v1/health/live", csrf: false);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AntiforgeryResponse_IsOverriddenByTheApiPolicy()
    {
        SessionResponse response = await new TestSession(_fixture.CreateClient())
            .SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task MissingAntiforgeryToken_400_CarriesTheSecurityHeaders()
    {
        SessionResponse response = await new TestSession(_fixture.CreateClient())
            .SendAsync(HttpMethod.Post, "api/v1/auth/login", new { userName = "ghost", password = "irrelevant" }, csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task NoSession_401_CarriesTheSecurityHeaders()
    {
        SessionResponse response = await new TestSession(_fixture.CreateClient())
            .SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task UnknownVersion_404_CarriesTheSecurityHeaders()
    {
        SessionResponse response = await new TestSession(_fixture.CreateClient())
            .SendAsync(HttpMethod.Get, "api/v9/health/live", csrf: false);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task RateLimited_429_CarriesTheSecurityHeaders()
    {
        Dictionary<string, string> settings = new()
        {
            ["RateLimiting:WindowSeconds"] = "3600",
            ["RateLimiting:AnonymousRequests"] = "1",
        };
        HttpClient client = _fixture.CreateIsolatedClient(
            settings,
            services => services.AddSingleton<IStartupFilter, ClientAddressProbe>(),
            out _);
        TestSession anonymous = new(client);
        Dictionary<string, string> address = new() { [ClientAddressProbe.Header] = "203.0.113.50" };

        await anonymous.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false, headers: address);
        SessionResponse rejected = await anonymous.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false, headers: address);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.Status);
        AssertSecurityHeaders(rejected);
    }

    [Fact]
    public async Task UnhandledException_500_CarriesTheSecurityHeaders()
    {
        HttpClient client = _fixture.CreateIsolatedClient(
            services => services.AddSingleton<IStartupFilter, ErrorProbe>(),
            out _);

        SessionResponse response = await new TestSession(client)
            .SendAsync(HttpMethod.Get, ErrorProbe.ThrowPath.TrimStart('/'), csrf: false);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public void Kestrel_DoesNotAdvertiseTheServer()
    {
        KestrelServerOptions kestrel = _fixture.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.False(kestrel.AddServerHeader);
    }

    [Theory]
    [InlineData("https://localhost", true)]
    [InlineData("http://localhost", false)]
    public async Task AntiforgeryCookie_IsSecure_OnlyOverHttps(string baseAddress, bool secure)
    {
        HttpClient client = _fixture.CreateIsolatedClient(_ => { }, out _);
        client.BaseAddress = new Uri(baseAddress);

        SessionResponse response = await new TestSession(client)
            .SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        string cookie = Assert.Single(response.SetCookies, c => c.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.Equal(secure, cookie.Split(';').Any(p => p.Trim().Equals("secure", StringComparison.OrdinalIgnoreCase)));
    }

    private static void AssertSecurityHeaders(SessionResponse response)
    {
        foreach ((string name, string value) in Expected)
        {
            string actual = Assert.Contains(name, response.Headers);
            Assert.True(
                string.Equals(value, actual, StringComparison.Ordinal),
                string.Create(CultureInfo.InvariantCulture, $"{name}: expected '{value}', got '{actual}' on {(int)response.Status}"));
        }
    }
}
