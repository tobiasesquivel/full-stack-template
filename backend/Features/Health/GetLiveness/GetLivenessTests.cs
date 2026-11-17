using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Health.GetLiveness;

public sealed class GetLivenessTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetLivenessTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Live_Returns200_WithLiveStatus()
    {
        using HttpClient client = _fixture.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("live", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task Live_Returns200_WhenDatabaseUnreachable()
    {
        using HttpClient client = _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = TestDatabases.Unreachable },
            TestDatabases.SkipInitialization,
            out _);
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("live", body.RootElement.GetProperty("status").GetString());
    }
}
