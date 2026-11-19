using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Projects.ApplyTaskEvent;

public sealed class ApplyTaskEventTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public ApplyTaskEventTests(PostgresFixture postgres, RabbitMqFixture broker)
    {
        _broker = broker.Uri;
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task TheRelay_CarriesEachTaskEvent_IntoTheProjectCounters()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        await alice.CreateTaskAsync("Abierta", project);
        string done = await alice.CreateTaskAsync("Hecha", project);
        string gone = await alice.CreateTaskAsync("Borrada", project);
        await TaskCounterScenario.SetCompletedAsync(alice, done, true);
        await TaskCounterScenario.SetCompletedAsync(alice, gone, true);
        SessionResponse deleted = await alice.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{gone}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        await alice.CreateTaskAsync("Suelta");

        Assert.Equal((0, 0), await CountersAsync(api, project));
        await api.DrainOutboxAsync();

        Assert.Equal((2, 1), await CountersAsync(api, project));
        Assert.Equal(await api.Pair.Monolith.Services.CountedTasksAsync(), await api.Pair.Monolith.Services.AllTaskCountersAsync());
    }

    [Fact]
    public async Task ADuplicateDelivery_CountsOnce()
    {
        FailOnceMarkingProcessed failure = new();
        IsolatedApi api = await StartAsync(services => services.ConfigureDbContext<TasksDbContext>((_, options) => options.AddInterceptors(failure)));
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        await alice.CreateTaskAsync("Comprar pan", project);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Relay.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal((1, 0), await CountersAsync(api, project));
        await api.DrainOutboxAsync();

        Assert.Equal((1, 0), await CountersAsync(api, project));
        Assert.Equal(1, await InboxRowsAsync(api));
    }

    [Fact]
    public async Task TheSameMessageTwice_CountsOnce()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        Guid message = Guid.NewGuid();

        using HttpResponseMessage first = await PutAsync(api, message, "tasks.task-created", project);
        using HttpResponseMessage second = await PutAsync(api, message, "tasks.task-created", project);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, second.StatusCode));
        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task EventsOutOfOrder_EndInTheSameCounters()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");

        using HttpResponseMessage deleted = await PutAsync(api, Guid.NewGuid(), "tasks.task-deleted", project, wasCompleted: true);
        Assert.Equal((-1, -1), await CountersAsync(api, project));
        using HttpResponseMessage completed = await PutAsync(api, Guid.NewGuid(), "tasks.task-completed", project);
        using HttpResponseMessage created = await PutAsync(api, Guid.NewGuid(), "tasks.task-created", project);

        Assert.Equal((0, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task AnEventForAnUnknownProject_HasNoEffect()
    {
        IsolatedApi api = await StartAsync();
        string unknown = Guid.NewGuid().ToString();

        using HttpResponseMessage response = await PutAsync(api, Guid.NewGuid(), "tasks.task-created", unknown);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await api.Pair.Monolith.Services.TaskCountersAsync(unknown));
    }

    [Fact]
    public async Task AnUnknownType_IsIgnored_AndAnUnknownVersion_Answers400()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");

        using HttpResponseMessage unknownType = await PutAsync(api, Guid.NewGuid(), "tasks.task-renamed", project);
        using HttpResponseMessage unknownVersion = await PutAsync(api, Guid.NewGuid(), "tasks.task-created", project, version: 2);

        Assert.Equal(HttpStatusCode.NoContent, unknownType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownVersion.StatusCode);
        await AssertCodeAsync(unknownVersion, "Project.UnreadableTaskEvent");
        Assert.Equal((0, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task WhileTheCountersAreRebuilt_AnEventAnswers503_AndCountsOnceAfterwards()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        Guid message = Guid.NewGuid();

        await using (NpgsqlConnection rebuild = await TestDatabases.OpenAsync(api.Database))
        {
            await using NpgsqlTransaction transaction = await rebuild.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand exclusive = new("SELECT pg_advisory_xact_lock(7164253024)", rebuild, transaction);
            await exclusive.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            using HttpResponseMessage blocked = await PutAsync(api, message, "tasks.task-created", project);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
            await AssertCodeAsync(blocked, "Project.TaskCountersRebuilding");
            Assert.Equal((0, 0), await CountersAsync(api, project));
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        using HttpResponseMessage retried = await PutAsync(api, message, "tasks.task-created", project);

        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task WithTheMonolithDown_TheEventStaysPending_AndArrivesLater()
    {
        RemoteFault monolithDown = RemoteFault.Unreachable(RemoteFault.Path("/internal/v1/task-events"));
        monolithDown.Active = false;
        IsolatedApi api = await StartAsync(interceptRemote: monolithDown.Intercept);
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        string task = await alice.CreateTaskAsync("Comprar pan", project);
        monolithDown.Active = true;

        await api.Relay.RunOnceAsync(TestContext.Current.CancellationToken);

        OutboxRow pending = Assert.Single(await api.Pair.Tasks.Services.OutboxRowsAsync(task));
        Assert.Equal((1, null), (pending.Attempts, pending.ProcessedAt));
        Assert.Equal((0, 0), await CountersAsync(api, project));

        monolithDown.Active = false;
        api.Clock.Advance(TimeSpan.FromSeconds(3));
        await api.DrainOutboxAsync();

        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static async Task<HttpResponseMessage> PutAsync(
        IsolatedApi api,
        Guid messageId,
        string type,
        string projectId,
        bool wasCompleted = false,
        int version = 1)
    {
        using HttpClient monolith = api.Pair.CreateMonolithClient();
        return await monolith.PutAsJsonAsync(
            $"internal/v1/task-events/{messageId}",
            new
            {
                type,
                version,
                occurredAt = DateTimeOffset.UtcNow,
                payload = new { taskId = Guid.NewGuid(), ownerId = "someone", projectId, wasCompleted },
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task<(int Tasks, int Completed)> CountersAsync(IsolatedApi api, string projectId) =>
        await api.Pair.Monolith.Services.TaskCountersAsync(projectId) ?? throw new InvalidOperationException($"Project {projectId} has no task counters.");

    private static Task<long> InboxRowsAsync(IsolatedApi api) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM projects."InboxMessages" """);

    private async Task<IsolatedApi> StartAsync(
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, configure, interceptRemote);
        _apis.Add(api);
        return api;
    }
}
