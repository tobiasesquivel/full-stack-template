using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Bus;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Projects.ApplyTaskEvent;

public sealed class ApplyTaskEventTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly Uri _broker;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public ApplyTaskEventTests(PostgresFixture postgres, RabbitMqFixture broker)
    {
        _broker = broker.Uri;
        _rabbit = broker;
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
        await api.DrainAsync();

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.RelayOf<TasksDbContext>().RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal((1, 0), await Eventually.Async(() => CountersAsync(api, project), counters => counters == (1, 0), TimeSpan.FromSeconds(30)));
        await api.DrainAsync();

        Assert.Equal((1, 0), await CountersAsync(api, project));
        Assert.Equal(1, await InboxRowsAsync(api));
    }

    [Fact]
    public async Task TheSameMessageTwice_CountsOnce()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await BrokerAsync(api);
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        Guid message = Guid.NewGuid();

        await broker.PublishAsync("tasks.task-created", message, new { projectId = project });
        await broker.PublishAsync("tasks.task-created", message, new { projectId = project });
        await ConsumedAsync(api, broker);

        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task EventsOutOfOrder_EndInTheSameCounters()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await BrokerAsync(api);
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");

        await broker.PublishAsync("tasks.task-deleted", Guid.NewGuid(), new { projectId = project, wasCompleted = true });
        await ConsumedAsync(api, broker);
        Assert.Equal((-1, -1), await CountersAsync(api, project));
        await broker.PublishAsync("tasks.task-completed", Guid.NewGuid(), new { projectId = project });
        await broker.PublishAsync("tasks.task-created", Guid.NewGuid(), new { projectId = project });
        await ConsumedAsync(api, broker);

        Assert.Equal((0, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task AnEventForAnUnknownProject_HasNoEffect()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await BrokerAsync(api);
        string unknown = Guid.NewGuid().ToString();
        Guid message = Guid.NewGuid();

        await broker.PublishAsync("tasks.task-created", message, new { projectId = unknown });
        await ConsumedAsync(api, broker);

        Assert.Equal(1, await InboxRowsAsync(api, message));
        Assert.Null(await api.Pair.Monolith.Services.TaskCountersAsync(unknown));
    }

    [Fact]
    public async Task AnEventTypeTheQueueDoesNotBind_NeverReachesTheConsumer()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await BrokerAsync(api);
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        Guid renamed = Guid.NewGuid();

        await broker.PublishAsync("tasks.task-renamed", renamed, new { projectId = project });
        await broker.PublishAsync("tasks.task-created", Guid.NewGuid(), new { projectId = project });
        await ConsumedAsync(api, broker);

        Assert.Equal(0, await InboxRowsAsync(api, renamed));
        Assert.Equal(0u, await broker.CountAsync(TaskEventsBroker.DeadLetterQueue));
        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task WhileTheCountersAreRebuilt_AnEventWaits_AndCountsOnceAfterwards()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await BrokerAsync(api);
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        Guid message = Guid.NewGuid();

        await using (NpgsqlConnection rebuild = await TestDatabases.OpenAsync(api.Database))
        {
            await using NpgsqlTransaction transaction = await rebuild.BeginTransactionAsync(TestContext.Current.CancellationToken);
            await using NpgsqlCommand exclusive = new("SELECT pg_advisory_xact_lock(7164253024)", rebuild, transaction);
            await exclusive.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            await broker.PublishAsync("tasks.task-created", message, new { projectId = project });
            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

            Assert.Equal((0, 0), await CountersAsync(api, project));
            Assert.Equal(0, await InboxRowsAsync(api, message));
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, await Eventually.Async(() => InboxRowsAsync(api, message), rows => rows == 1, Wait));
        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    [Fact]
    public async Task WithTheMonolithDownForADay_TheEventWaitsInTheQueue_AndCountsWhenItReturns()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        string task = await alice.CreateTaskAsync("Comprar pan", project);
        await MonolithListener.StopAsync(api);

        await api.PublishOutboxAsync<TasksDbContext>();
        api.Clock.Advance(TimeSpan.FromDays(1));
        await api.PublishOutboxAsync<TasksDbContext>();

        OutboxRow published = Assert.Single(await api.Pair.Tasks.Services.OutboxRowsAsync(task));
        Assert.Equal((0, null), (published.Attempts, published.FailedAt));
        Assert.NotNull(published.ProcessedAt);
        Assert.Equal((0, 0), await CountersAsync(api, project));

        await MonolithListener.StartAsync(api);
        await api.DrainAsync();

        Assert.Equal((1, 0), await CountersAsync(api, project));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static async Task ConsumedAsync(IsolatedApi api, TaskEventsBroker broker)
    {
        Guid marker = Guid.NewGuid();
        await broker.PublishAsync("tasks.task-created", marker, new { projectId = Guid.NewGuid() });
        Assert.Equal(1, await Eventually.Async(() => InboxRowsAsync(api, marker), rows => rows == 1, Wait));
    }

    private static async Task<(int Tasks, int Completed)> CountersAsync(IsolatedApi api, string projectId) =>
        await api.Pair.Monolith.Services.TaskCountersAsync(projectId) ?? throw new InvalidOperationException($"Project {projectId} has no task counters.");

    private static Task<long> InboxRowsAsync(IsolatedApi api) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM projects."InboxMessages" """);

    private static Task<long> InboxRowsAsync(IsolatedApi api, Guid messageId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM projects."InboxMessages" WHERE "MessageId" = {messageId}""");

    private async Task<TaskEventsBroker> BrokerAsync(IsolatedApi api)
    {
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return broker;
    }

    private async Task<IsolatedApi> StartAsync(
        Action<IServiceCollection>? configure = null,
        Func<HttpRequestMessage, HttpMessageHandler, HttpMessageHandler?>? interceptRemote = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, configure, interceptRemote);
        _apis.Add(api);
        return api;
    }
}
