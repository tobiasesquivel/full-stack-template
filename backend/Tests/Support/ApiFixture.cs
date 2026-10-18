using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TemplateApp.Tests.Support;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _connectionString;
    private WebApplicationFactory<Program>? _factory;
    private readonly List<WebApplicationFactory<Program>> _isolatedFactories = new();

    public ApiFixture(PostgresFixture postgres)
    {
        _connectionString = postgres.ConnectionString;
    }

    public HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    public IServiceProvider Services => _factory!.Services;

    public HttpClient CreateIsolatedClient(out IServiceProvider services)
    {
        WebApplicationFactory<Program> isolated = CreateFactory(_connectionString);

        _isolatedFactories.Add(isolated);
        services = isolated.Services;
        return isolated.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    public ValueTask InitializeAsync()
    {
        _factory = CreateFactory(_connectionString);
        _ = _factory.Services;
        return ValueTask.CompletedTask;
    }

    public static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new TestApiFactory(connectionString);

    private sealed class TestApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Session:AbsoluteExpirationDays", "7");
            builder.UseSetting("Session:IdleTimeoutMinutes", "30");
        }
    }

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        foreach (WebApplicationFactory<Program> isolated in _isolatedFactories)
            isolated.Dispose();

        return ValueTask.CompletedTask;
    }
}
