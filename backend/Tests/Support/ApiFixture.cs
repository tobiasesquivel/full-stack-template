using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TemplateApp.Tests.Support;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _connectionString;
    private ApiPair? _api;
    private readonly List<ApiPair> _isolated = new();

    public ApiFixture(PostgresFixture postgres)
    {
        _connectionString = postgres.ConnectionString;
    }

    public HttpClient CreateClient() => _api!.CreateClient();

    public IServiceProvider Services => _api!.Monolith.Services;

    public IServiceProvider TasksServices => _api!.Tasks.Services;

    public HttpClient CreateMonolithClient() => _api!.CreateMonolithClient();

    public HttpClient CreateTasksClient() => _api!.CreateTasksClient();

    public ApiPair CreateIsolatedApi(RemoteFault fault) =>
        CreateIsolatedApi(new Dictionary<string, string>(), _ => { }, fault.Intercept);

    public ApiPair CreateIsolatedApi(IReadOnlyDictionary<string, string> settings, RemoteFault fault) =>
        CreateIsolatedApi(settings, _ => { }, fault.Intercept);

    public HttpClient CreateIsolatedClient(out IServiceProvider services) =>
        CreateIsolatedClient(new Dictionary<string, string>(), out services);

    public HttpClient CreateIsolatedClient(IReadOnlyDictionary<string, string> settings, out IServiceProvider services) =>
        CreateIsolatedClient(settings, _ => { }, out services);

    public HttpClient CreateIsolatedClient(Action<IServiceCollection> configureServices, out IServiceProvider services) =>
        CreateIsolatedClient(new Dictionary<string, string>(), configureServices, out services);

    public HttpClient CreateIsolatedClient(
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices,
        out IServiceProvider services)
    {
        ApiPair isolated = CreateIsolatedApi(settings, configureServices);
        services = isolated.Monolith.Services;
        return isolated.CreateClient();
    }

    public HttpClient CreateIsolatedTasksClient(Action<IServiceCollection> configureServices, out IServiceProvider tasksServices)
    {
        ApiPair isolated = CreateIsolatedApi(new Dictionary<string, string>(), configureServices);
        tasksServices = isolated.Tasks.Services;
        return isolated.CreateClient();
    }

    public ApiPair CreateIsolatedApi(
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null,
        bool start = true)
    {
        ApiPair isolated = new(_connectionString, settings, configureServices, interceptRemote);
        _isolated.Add(isolated);
        if (start)
            isolated.Start();
        return isolated;
    }

    public ValueTask InitializeAsync()
    {
        _api = new ApiPair(_connectionString, new Dictionary<string, string>(), _ => { });
        _api.Start();
        return ValueTask.CompletedTask;
    }

    public static WebApplicationFactory<MonolithHost> CreateFactory(string connectionString) =>
        new ApiPair.MonolithFactory(connectionString, new Dictionary<string, string>(), _ => { });

    public ValueTask DisposeAsync()
    {
        _api?.Dispose();
        foreach (ApiPair isolated in _isolated)
            isolated.Dispose();

        return ValueTask.CompletedTask;
    }
}
