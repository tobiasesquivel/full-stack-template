using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Tests.Support;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace TemplateApp.Tests.Remote;

public sealed class RemoteResilienceTests : IDisposable
{
    private const string SessionChecks = "/internal/v1/session-checks";
    private const string Recovered = "recovered";

    private readonly WireMockServer _monolith = LoopbackWireMock.Start();

    [Fact]
    public async Task TransientFailure_TheGetIsRetried_AndTheCallerSeesTheAnswer()
    {
        _monolith.Given(OwnerProjects().UsingGet()).InScenario("flaky").WillSetStateTo(Recovered)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        _monolith.Given(OwnerProjects().UsingGet()).InScenario("flaky").WhenStateIs(Recovered)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(new { isArchived = true }));
        await using ServiceProvider services = Services<ProjectsClient>(RemoteRetry.Idempotent);

        ProjectAssignment? assignment = await services.GetRequiredService<ProjectsClient>()
            .FindAssignableAsync("alice", Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(new ProjectAssignment(IsArchived: true), assignment);
        Assert.Equal(2, _monolith.LogEntries.Count());
    }

    [Fact]
    public async Task TransientFailure_TheSessionCheckIsRetried_WithTheSameBody()
    {
        _monolith.Given(Request.Create().WithPath(SessionChecks).UsingPost()).InScenario("flaky").WillSetStateTo(Recovered)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        _monolith.Given(Request.Create().WithPath(SessionChecks).UsingPost()).InScenario("flaky").WhenStateIs(Recovered)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(new { permissions = new[] { "tasks:read" } }));
        await using ServiceProvider services = Services<AuthSessionClient>(RemoteRetry.Idempotent);
        Guid session = Guid.NewGuid();

        IReadOnlyList<string>? permissions = await services.GetRequiredService<AuthSessionClient>()
            .CheckAsync("alice", session, TestContext.Current.CancellationToken);

        Assert.Equal(["tasks:read"], permissions);
        string[] bodies = [.. _monolith.LogEntries.Select(entry => entry.RequestMessage.Body ?? string.Empty)];
        Assert.Equal(2, bodies.Length);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains(session.ToString(), bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientDeclaredNeverRetry_ATransientFailureReachesTheCaller_AfterOneCall()
    {
        _monolith.Given(Request.Create().WithPath(SessionChecks).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        await using ServiceProvider services = Services<AuthSessionClient>(RemoteRetry.Never);

        await Assert.ThrowsAsync<RemoteServiceUnavailableException>(() => services.GetRequiredService<AuthSessionClient>()
            .CheckAsync("alice", Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Single(_monolith.LogEntries);
    }

    [Fact]
    public async Task PersistentFailure_OpensTheCircuit_ThenCallsFailWithoutReachingTheServer_UntilAProbeSucceeds()
    {
        _monolith.Given(OwnerProjects().UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        await using ServiceProvider services = Services<ProjectsClient>(
            RemoteRetry.Idempotent,
            ("BreakerMinimumThroughput", "2"),
            ("BreakDuration", "00:00:02"));
        ProjectsClient projects = services.GetRequiredService<ProjectsClient>();
        CancellationToken ct = TestContext.Current.CancellationToken;

        RemoteServiceUnavailableException opening = await Assert.ThrowsAsync<RemoteServiceUnavailableException>(
            () => projects.FindAssignableAsync("alice", Guid.NewGuid(), ct));
        int callsWhenOpened = _monolith.LogEntries.Count();
        RemoteServiceUnavailableException open = await Assert.ThrowsAsync<RemoteServiceUnavailableException>(
            () => projects.FindAssignableAsync("alice", Guid.NewGuid(), ct));
        int callsWhileOpen = _monolith.LogEntries.Count();

        _monolith.Reset();
        _monolith.Given(OwnerProjects().UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(new { isArchived = false }));
        await Task.Delay(TimeSpan.FromMilliseconds(2300), ct);
        ProjectAssignment? probe = await projects.FindAssignableAsync("alice", Guid.NewGuid(), ct);
        ProjectAssignment? closed = await projects.FindAssignableAsync("alice", Guid.NewGuid(), ct);

        Assert.Equal(2, callsWhenOpened);
        Assert.IsType<BrokenCircuitException>(opening.InnerException);
        Assert.Equal(callsWhenOpened, callsWhileOpen);
        Assert.IsType<BrokenCircuitException>(open.InnerException);
        Assert.Equal(new ProjectAssignment(IsArchived: false), probe);
        Assert.Equal(new ProjectAssignment(IsArchived: false), closed);
        Assert.Equal(2, _monolith.LogEntries.Count());
    }

    [Fact]
    public async Task Bulkhead_ACallOverTheConcurrencyLimit_IsRejectedWithoutReachingTheServer()
    {
        HeldServer server = new();
        await using ServiceProvider services = Services<ProjectsClient>(RemoteRetry.Idempotent, server, ("MaxConcurrentRequests", "1"));
        ProjectsClient projects = services.GetRequiredService<ProjectsClient>();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Task<ProjectAssignment?> first = projects.FindAssignableAsync("alice", Guid.NewGuid(), ct);
        await server.Arrived.Task.WaitAsync(ct);
        RemoteServiceUnavailableException second = await Assert.ThrowsAsync<RemoteServiceUnavailableException>(
            () => projects.FindAssignableAsync("bob", Guid.NewGuid(), ct));
        server.Release.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { isArchived = false }) });

        Assert.IsType<RateLimiterRejectedException>(second.InnerException);
        Assert.Equal(new ProjectAssignment(IsArchived: false), await first);
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public async Task HeadersArriveButTheBodyStalls_TheAttemptTimeoutCoversTheBody_AndTheCallerGets503()
    {
        StalledBodyServer server = new();
        await using ServiceProvider services = Services<ProjectsClient>(
            RemoteRetry.Idempotent,
            server,
            ("Timeout", "00:00:00.300"),
            ("TotalTimeout", "00:00:02"),
            ("Retries", "1"));
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(10));

        RemoteServiceUnavailableException stalled = await Assert.ThrowsAsync<RemoteServiceUnavailableException>(
            () => services.GetRequiredService<ProjectsClient>().FindAssignableAsync("alice", Guid.NewGuid(), guard.Token));

        Assert.IsType<TimeoutRejectedException>(stalled.InnerException);
        Assert.Equal(2, server.Calls);
    }

    [Fact]
    public async Task PersistentTooManyRequests_AfterTheRetries_TheCallerGets503()
    {
        _monolith.Given(OwnerProjects().UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.TooManyRequests).WithHeader("Retry-After", "86400"));
        await using ServiceProvider services = Services<ProjectsClient>(RemoteRetry.Idempotent, ("Retries", "1"));

        await Assert.ThrowsAsync<RemoteServiceUnavailableException>(() => services.GetRequiredService<ProjectsClient>()
            .FindAssignableAsync("alice", Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal(2, _monolith.LogEntries.Count());
    }

    public void Dispose() => _monolith.Dispose();

    private static IRequestBuilder OwnerProjects() =>
        Request.Create().WithPath(new WildcardMatcher("/internal/v1/owners/*/projects/*"));

    private ServiceProvider Services<TClient>(RemoteRetry retry, params (string Key, string Value)[] settings)
        where TClient : class =>
        Services<TClient>(retry, primary: null, settings);

    private ServiceProvider Services<TClient>(RemoteRetry retry, HttpMessageHandler? primary, params (string Key, string Value)[] settings)
        where TClient : class
    {
        string section = $"{RemoteServiceOptions.Section}:{TasksFeature.MonolithService}";
        Dictionary<string, string?> values = new()
        {
            [$"{section}:BaseAddress"] = $"{_monolith.Url}/",
            [$"{section}:RetryDelay"] = "00:00:00.010",
        };
        foreach ((string key, string value) in settings)
            values[$"{section}:{key}"] = value;

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddLogging();
        services.AddMetrics();
        services.AddHttpContextAccessor();
        IHttpClientBuilder client = services.AddRemoteClient<TClient>(TasksFeature.MonolithService, retry);
        if (primary is not null)
            client.ConfigurePrimaryHttpMessageHandler(() => primary);
        return services.BuildServiceProvider();
    }

    private sealed class HeldServer : HttpMessageHandler
    {
        private int _calls;

        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Arrived.TrySetResult();
            return Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class StalledBodyServer : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            System.IO.Pipelines.Pipe body = new();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body.Reader.AsStream()),
            });
        }
    }
}
