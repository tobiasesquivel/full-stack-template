using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace TemplateApp.Tests.Support;

public sealed class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private PostgreSqlContainer? _postgres;
    private string? _externalConnection;
    private string? _connectionString;
    private readonly List<WebApplicationFactory<Program>> _isolatedFactories = new();

    public HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    public IServiceProvider Services => _factory!.Services;

    public HttpClient CreateIsolatedClient(out IServiceProvider services)
    {
        WebApplicationFactory<Program> isolated = CreateFactory(_connectionString!);

        _isolatedFactories.Add(isolated);
        services = isolated.Services;
        return isolated.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    public async ValueTask InitializeAsync()
    {
        _externalConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");

        if (!string.IsNullOrWhiteSpace(_externalConnection))
        {
            _connectionString = _externalConnection;
        }
        else
        {
            _postgres = new PostgreSqlBuilder("postgres:18")
                .WithDatabase("templateapp_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
        }

        _factory = CreateFactory(_connectionString);
        _ = _factory.Services;
    }

    public static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new TestApiFactory(connectionString);

    private sealed class TestApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Jwt:Key", "test-jwt-key-must-be-at-least-32-chars-long!!");
            builder.UseSetting("Jwt:Issuer", "https://tests.templateapp.local");
            builder.UseSetting("Jwt:Audience", "api://tests");
            builder.UseSetting("Jwt:AccessTokenExpirationMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpirationDays", "7");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        foreach (WebApplicationFactory<Program> isolated in _isolatedFactories)
            isolated.Dispose();

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

}
