using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Hosts.Monolith;
using TemplateApp.Hosts.Tasks;
using TemplateApp.Infrastructure.Persistence.Initialization;
using Wolverine;

namespace TemplateApp.Tests.Support.Hosts;

public sealed class ApiPair : IDisposable
{
    public const string MonolithAuthority = "monolith";
    public const string TasksAuthority = "tasks";
    public const string BrokerSetting = "Messaging:RabbitMqUri";
    private const string TasksPrefix = "/api/v1/tasks";
    private const string PermissionsDeclarationPrefix = "/internal/v1/feature-permissions/";
    private static readonly TimeSpan PermissionsDeclarationTimeout = TimeSpan.FromSeconds(30);

    private readonly MonolithFactory _monolith;
    private readonly TasksFactory _tasks;
    private readonly bool _kestrel;
    private readonly TaskCompletionSource<Func<HttpMessageHandler>> _monolithStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private KestrelRoute? _kestrelRoute;

    public ApiPair(
        string connectionString,
        IReadOnlyDictionary<string, string> settings,
        Action<IServiceCollection> configureServices,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null,
        bool kestrel = false,
        Action<IServiceCollection>? configureTasks = null)
    {
        _kestrel = kestrel;
        _monolith = new MonolithFactory(connectionString, settings, services =>
        {
            configureServices(services);
            RouteRemoteCalls(services, interceptRemote);
            services.AddHostedService(provider => new StartedSignal(provider, _monolithStarted, address => _kestrelRoute = new KestrelRoute(address)));
        });
        _tasks = new TasksFactory(connectionString, settings, services =>
        {
            configureServices(services);
            configureTasks?.Invoke(services);
            RouteRemoteCalls(services, interceptRemote);
        });
    }

    public WebApplicationFactory<MonolithHost> Monolith => _monolith;

    public WebApplicationFactory<TasksHost> Tasks => _tasks;

    public HttpClient CreateClient() => _kestrel
        ? _monolith.CreateDefaultClient()
        : new(new InMemoryRoutes(request => request.RequestUri!.AbsolutePath.StartsWith(TasksPrefix, StringComparison.OrdinalIgnoreCase)
            ? _tasks.Server
            : _monolith.Server))
        {
            BaseAddress = new Uri("http://localhost"),
        };

    public HttpClient CreateMonolithClient() => new(new InMemoryRoutes(_ => _monolith.Server)) { BaseAddress = new Uri("http://localhost") };

    public HttpClient CreateTasksClient() => new(new InMemoryRoutes(_ => _tasks.Server)) { BaseAddress = new Uri("http://localhost") };

    public void Start()
    {
        if (_kestrel)
        {
            _monolith.UseKestrel(0);
            _monolith.StartServer();
        }

        _ = _monolith.Services;
        _ = _tasks.Services;
        if (_monolith.Services.GetServices<IHostedService>().OfType<DatabaseInitialization>().Any())
            TasksDeclared.WaitAsync(PermissionsDeclarationTimeout).GetAwaiter().GetResult();
    }

    public Task TasksDeclared => _tasks.Services.GetRequiredService<PermissionsDeclaration>().Declared;

    public static bool IsPermissionsDeclaration(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.StartsWith(PermissionsDeclarationPrefix, StringComparison.Ordinal);

    public void Dispose()
    {
        _monolith.Dispose();
        _tasks.Dispose();
        _kestrelRoute?.Release();
    }

    private void RouteRemoteCalls(IServiceCollection services, Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote) =>
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() =>
            new InterceptedRoutes(interceptRemote, new PairRoutes(this))));

    private HttpMessageHandler StartedMonolith()
    {
        if (!_monolithStarted.Task.IsCompletedSuccessfully)
            throw new HttpRequestException("The monolith has not started yet.");

        return _monolithStarted.Task.GetAwaiter().GetResult()();
    }

    internal static void ConfigureCommon(IWebHostBuilder builder, string connectionString, IReadOnlyDictionary<string, string> settings, Action<IServiceCollection> configureServices)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Session:AbsoluteExpirationDays", "7");
        builder.UseSetting("Session:IdleTimeoutMinutes", "30");
        builder.UseSetting("Services:Tasks:BaseAddress", $"http://{TasksAuthority}/");
        builder.UseSetting("Services:Monolith:BaseAddress", $"http://{MonolithAuthority}/");
        builder.UseSetting("Outbox:RelayEnabled", "false");
        builder.UseSetting(BrokerSetting, "amqp://localhost:5672/");
        foreach ((string key, string value) in settings)
            builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.UseWolverineSoloMode();
            if (!settings.ContainsKey(BrokerSetting))
                services.DisableAllExternalWolverineTransports();
            configureServices(services);
        });
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

    private sealed class StartedSignal(IServiceProvider services, TaskCompletionSource<Func<HttpMessageHandler>> started, Func<Uri, KestrelRoute> own) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            IServer server = services.GetRequiredService<IServer>();
            services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => started.TrySetResult(Route(server)));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Func<HttpMessageHandler> Route(IServer server)
        {
            if (server is TestServer memory)
                return memory.CreateHandler;

            Uri address = new(server.Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First());
            KestrelRoute kestrel = own(address);
            return () => kestrel;
        }
    }

    private sealed class KestrelRoute(Uri address) : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri original = request.RequestUri!;
            request.RequestUri = new UriBuilder(original) { Scheme = address.Scheme, Host = address.Host, Port = address.Port }.Uri;
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                request.RequestUri = original;
            }
        }

        public void Release() => base.Dispose(true);

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class PairRoutes(ApiPair pair) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpMessageHandler target = request.RequestUri!.Host switch
            {
                TasksAuthority => pair._tasks.Server.CreateHandler(),
                MonolithAuthority => pair.StartedMonolith(),
                string other => throw new InvalidOperationException($"No in-memory host answers {other}."),
            };
            using HttpMessageInvoker invoker = new(target);
            return await invoker.SendAsync(request, cancellationToken);
        }
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
