using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

    public HttpClient CreateIsolatedClient(out IServiceProvider services) =>
        CreateIsolatedClient(new Dictionary<string, string>(), out services);

    public HttpClient CreateIsolatedClient(IReadOnlyDictionary<string, string> settings, out IServiceProvider services) =>
        CreateIsolatedClient(settings, _ => { }, out services);

    public HttpClient CreateIsolatedClient(Action<IServiceCollection> configureServices, out IServiceProvider services) =>
        CreateIsolatedClient(new Dictionary<string, string>(), configureServices, out services);

    private HttpClient CreateIsolatedClient(
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices,
        out IServiceProvider services)
    {
        WebApplicationFactory<Program> isolated = new TestApiFactory(_connectionString, settings, configureServices);

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
        new TestApiFactory(connectionString, new Dictionary<string, string>(), _ => { });

    private sealed class TestApiFactory(
        string connectionString,
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Session:AbsoluteExpirationDays", "7");
            builder.UseSetting("Session:IdleTimeoutMinutes", "30");
            foreach ((string key, string value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureTestServices(configureServices);
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
