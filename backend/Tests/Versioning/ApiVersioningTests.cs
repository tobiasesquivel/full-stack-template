using System.Net;
using Asp.Versioning;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Versioning;

public sealed class ApiVersioningTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ApiVersioningTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task V1_Reports_TheSupportedVersions()
    {
        TestSession anonymous = new(_fixture.CreateClient());

        SessionResponse live = await anonymous.SendAsync(HttpMethod.Get, "api/v1/health/live");

        Assert.Equal(HttpStatusCode.OK, live.Status);
        Assert.Equal("1.0", Assert.Contains("api-supported-versions", live.Headers));
    }

    [Theory]
    [InlineData("api/v9/health/live")]
    [InlineData("api/health/live")]
    [InlineData("api/vabc/health/live")]
    public async Task UnknownOrMissingVersion_Returns404_HttpNotFound(string route)
    {
        TestSession anonymous = new(_fixture.CreateClient());

        SessionResponse response = await anonymous.SendAsync(HttpMethod.Get, route);

        Assert.DoesNotContain("api-supported-versions", response.Headers);
        ProblemAssert.Problem(response, HttpStatusCode.NotFound, "Http.NotFound");
    }

    [Fact]
    public async Task VersionQueryString_IsIgnored()
    {
        TestSession anonymous = new(_fixture.CreateClient());

        SessionResponse live = await anonymous.SendAsync(HttpMethod.Get, "api/v1/health/live?api-version=2.0");

        Assert.Equal(HttpStatusCode.OK, live.Status);
    }

    [Fact]
    public async Task EveryApiEndpoint_HasVersionMetadata()
    {
        using HttpClient client = _fixture.CreateClient();
        await client.GetAsync(new Uri("api/v1/health/live", UriKind.Relative));
        EndpointDataSource source = _fixture.Services.GetRequiredService<EndpointDataSource>();

        RouteEndpoint[] api = source.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(api);
        Assert.All(api, endpoint =>
        {
            Assert.StartsWith("/api/v{version:apiVersion}/", endpoint.RoutePattern.RawText, StringComparison.Ordinal);
            ApiVersionMetadata? metadata = endpoint.Metadata.GetMetadata<ApiVersionMetadata>();
            Assert.NotNull(metadata);
            Assert.False(metadata.IsApiVersionNeutral, endpoint.DisplayName);
        });
    }
}
