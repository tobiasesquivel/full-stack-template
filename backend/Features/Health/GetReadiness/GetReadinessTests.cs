using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Health.GetReadiness;

public sealed class GetReadinessTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetReadinessTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Ready_Returns200_WhenDatabaseReady()
    {
        using HttpClient client = _fixture.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_Body_ContainsNoInternals()
    {
        using HttpClient client = _fixture.CreateClient();
        string body = await (await client.GetAsync("/api/v1/health/ready")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Readiness_IsFalse_WhenDatabaseUnreachable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=54399;Database=unreachable;Username=postgres;Password=unreachable;Timeout=2;Command Timeout=2")
            .Options;

        using var db = new AppDbContext(options);
        var readiness = new GetReadiness(db);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool ready = await readiness.ExecuteAsync(timeout.Token);

        Assert.False(ready);
    }
}
