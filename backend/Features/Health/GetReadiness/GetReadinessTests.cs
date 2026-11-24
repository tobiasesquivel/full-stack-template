using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Features.Health.GetReadiness;

public sealed class GetReadinessTests : IClassFixture<ApiFixture>
{
    private const string ConnectionSetting = "ConnectionStrings:DefaultConnection";

    private readonly ApiFixture _fixture;
    private readonly PostgresFixture _postgres;
    private readonly RabbitMqFixture _rabbit;

    public GetReadinessTests(ApiFixture fixture, PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _fixture = fixture;
        _postgres = postgres;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task Ready_Returns200_WithEachDependencyHealthy()
    {
        using HttpClient client = ClientAgainst(
            _postgres.ConnectionString,
            services => services.PostConfigure<MessagingOptions>(options => options.RabbitMqUri = _rabbit.Uri));
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            new Dictionary<string, string> { ["database"] = "healthy", ["migrations"] = "healthy", ["rabbitmq"] = "healthy", ["broker"] = "healthy", ["broker-listeners"] = "healthy" },
            Checks(body));
    }

    [Fact]
    public async Task Ready_Returns200_WithRabbitMqUnreachable_AndMarksItDegraded()
    {
        using HttpClient client = _fixture.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ready", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.TryGetProperty("timestamp", out _));
        Assert.Equal(
            new Dictionary<string, string> { ["database"] = "healthy", ["migrations"] = "healthy", ["rabbitmq"] = "degraded", ["broker"] = "healthy", ["broker-listeners"] = "healthy" },
            Checks(body));
    }

    [Fact]
    public async Task Ready_Body_ContainsNoInternals()
    {
        using HttpClient client = _fixture.CreateClient();
        string body = await (await client.GetAsync("/api/v1/health/ready")).Content.ReadAsStringAsync();

        AssertNoInternals(body);
    }

    [Fact]
    public async Task Ready_Returns503_WhenDatabaseUnreachable()
    {
        using HttpClient client = ClientAgainst(TestDatabases.Unreachable);
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not-ready", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("unhealthy", Checks(body)["database"]);
    }

    [Fact]
    public async Task Ready_Returns503_WhenMigrationsPending()
    {
        string empty = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);
        using HttpClient client = ClientAgainst(empty);
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not-ready", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            new Dictionary<string, string> { ["database"] = "healthy", ["migrations"] = "unhealthy", ["rabbitmq"] = "degraded", ["broker"] = "healthy", ["broker-listeners"] = "healthy" },
            Checks(body));
    }

    [Fact]
    public async Task Ready_Body_ContainsNoInternals_WhenDatabaseRejectsTheConnection()
    {
        using HttpClient client = ClientAgainst(TestDatabases.WithWrongPassword(_postgres.ConnectionString));
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertNoInternals(body);
        Assert.DoesNotContain("28P01", body, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ready_LogsTheFailureReason_WhenDatabaseRejectsTheConnection()
    {
        CapturedLogs logs = new();
        using HttpClient client = ClientAgainst(
            TestDatabases.WithWrongPassword(_postgres.ConnectionString),
            services => services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs])));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(logs.Entries, entry =>
            entry.Category.Contains("HealthCheck", StringComparison.Ordinal)
            && entry.Message.StartsWith("Health check database ", StringComparison.Ordinal)
            && entry.Level == LogLevel.Error
            && entry.Exception is NpgsqlException);
    }

    [Fact]
    public async Task Ready_AnswersWithinTimeout_WhenDatabaseHangs()
    {
        using SilentServer silent = new();
        using HttpClient client = ClientAgainst(silent.ConnectionString);

        Stopwatch elapsed = Stopwatch.StartNew();
        using HttpResponseMessage response = await client.GetAsync("/api/v1/health/ready");
        elapsed.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"readiness tardó {elapsed.Elapsed}");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", Checks(body)["database"]);
    }

    private HttpClient ClientAgainst(string connectionString, Action<IServiceCollection>? configureServices = null) =>
        _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { [ConnectionSetting] = connectionString },
            services =>
            {
                TestDatabases.SkipInitialization(services);
                configureServices?.Invoke(services);
            },
            out _);

    private static Dictionary<string, string> Checks(JsonDocument body) =>
        body.RootElement.GetProperty("checks").EnumerateObject()
            .ToDictionary(check => check.Name, check => check.Value.GetString()!);

    private static void AssertNoInternals(string body)
    {
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
    }
}
