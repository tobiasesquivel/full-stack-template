using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Hosts.Tasks;

namespace TemplateApp.Tests.Support;

public sealed class ApiPair : IDisposable
{
    public const string MonolithAuthority = "monolith";
    public const string TasksAuthority = "tasks";
    private const string TasksPrefix = "/api/v1/tasks";

    private readonly MonolithFactory _monolith;
    private readonly TasksFactory _tasks;

    public ApiPair(
        string connectionString,
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null)
    {
        _monolith = new MonolithFactory(connectionString, settings, services =>
        {
            configureServices(services);
            RouteRemoteCalls(services, interceptRemote);
        });
        _tasks = new TasksFactory(connectionString, settings, services =>
        {
            configureServices(services);
            RouteRemoteCalls(services, interceptRemote);
        });
    }

    public WebApplicationFactory<MonolithHost> Monolith => _monolith;

    public WebApplicationFactory<TasksHost> Tasks => _tasks;

    public HttpClient CreateClient() =>
        new(new InMemoryRoutes(request => request.RequestUri!.AbsolutePath.StartsWith(TasksPrefix, StringComparison.OrdinalIgnoreCase)
            ? _tasks.Server
            : _monolith.Server))
        {
            BaseAddress = new Uri("http://localhost"),
        };

    public HttpClient CreateMonolithClient() => new(new InMemoryRoutes(_ => _monolith.Server)) { BaseAddress = new Uri("http://localhost") };

    public HttpClient CreateTasksClient() => new(new InMemoryRoutes(_ => _tasks.Server)) { BaseAddress = new Uri("http://localhost") };

    public void Start()
    {
        _ = _monolith.Services;
        _ = _tasks.Services;
    }

    public void Dispose()
    {
        _monolith.Dispose();
        _tasks.Dispose();
    }

    private void RouteRemoteCalls(IServiceCollection services, Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote) =>
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() =>
            new InterceptedRoutes(interceptRemote, new InMemoryRoutes(request => request.RequestUri!.Host switch
            {
                TasksAuthority => _tasks.Server,
                MonolithAuthority => _monolith.Server,
                string other => throw new InvalidOperationException($"No in-memory host answers {other}."),
            }))));

    internal static void ConfigureCommon(IWebHostBuilder builder, string connectionString, IReadOnlyDictionary<string, string> settings, Action<IServiceCollection> configureServices)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Session:AbsoluteExpirationDays", "7");
        builder.UseSetting("Session:IdleTimeoutMinutes", "30");
        builder.UseSetting("Services:Tasks:BaseAddress", $"http://{TasksAuthority}/");
        builder.UseSetting("Services:Monolith:BaseAddress", $"http://{MonolithAuthority}/");
        foreach ((string key, string value) in settings)
            builder.UseSetting(key, value);
        builder.ConfigureTestServices(configureServices);
    }

    internal sealed class MonolithFactory(
        string connectionString,
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices) : WebApplicationFactory<MonolithHost>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            ConfigureCommon(builder, connectionString, settings, configureServices);
    }

    private sealed class TasksFactory(
        string connectionString,
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices) : WebApplicationFactory<TasksHost>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            ConfigureCommon(builder, connectionString, settings, configureServices);
    }

    private sealed class InterceptedRoutes(Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? intercept, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpMessageHandler? replacement = intercept?.Invoke(request, InnerHandler!);
            if (replacement is null)
                return await base.SendAsync(request, cancellationToken);

            using HttpMessageInvoker invoker = new(replacement, disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }
}
