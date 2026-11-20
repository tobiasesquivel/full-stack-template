using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Infrastructure.Http;
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

    private readonly WireMockServer _monolith = WireMockServer.Start();

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
            ("BreakDuration", "00:00:00.500"));
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
        await Task.Delay(TimeSpan.FromMilliseconds(700), ct);
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
        _monolith.Given(OwnerProjects().UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithBodyAsJson(new { isArchived = false }).WithDelay(TimeSpan.FromMilliseconds(500)));
        await using ServiceProvider services = Services<ProjectsClient>(RemoteRetry.Idempotent, ("MaxConcurrentRequests", "1"));
        ProjectsClient projects = services.GetRequiredService<ProjectsClient>();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Task<ProjectAssignment?> first = projects.FindAssignableAsync("alice", Guid.NewGuid(), ct);
        await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        RemoteServiceUnavailableException second = await Assert.ThrowsAsync<RemoteServiceUnavailableException>(
            () => projects.FindAssignableAsync("bob", Guid.NewGuid(), ct));

        Assert.IsType<RateLimiterRejectedException>(second.InnerException);
        Assert.Equal(new ProjectAssignment(IsArchived: false), await first);
        Assert.Single(_monolith.LogEntries);
    }

    public void Dispose() => _monolith.Dispose();

    private static IRequestBuilder OwnerProjects() =>
        Request.Create().WithPath(new WildcardMatcher("/internal/v1/owners/*/projects/*"));

    private ServiceProvider Services<TClient>(RemoteRetry retry, params (string Key, string Value)[] settings)
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
        services.AddRemoteClient<TClient>(TasksFeature.MonolithService, retry);
        return services.BuildServiceProvider();
    }
}
