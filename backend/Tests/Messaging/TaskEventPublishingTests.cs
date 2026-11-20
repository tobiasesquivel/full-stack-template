using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class TaskEventPublishingTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];
    private RabbitMqContainer? _ownRabbit;

    public TaskEventPublishingTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task TheRelay_PublishesTheEventToRabbitMq_BeforeMarkingItProcessed()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        await broker.BindProbeAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string task = await alice.CreateTaskAsync("Comprar pan", await alice.CreateProjectAsync("Casa"));

        await api.PublishOutboxAsync();

        OutboxRow row = Assert.Single(await api.Pair.Tasks.Services.OutboxRowsAsync(task));
        Assert.Equal((0, null), (row.Attempts, row.FailedAt));
        Assert.NotNull(row.ProcessedAt);
        Assert.Equal(1u, await broker.CountAsync(TaskEventsBroker.Probe));
        BasicGetResult message = (await broker.TakeAsync(TaskEventsBroker.Probe))!;
        Assert.Equal("tasks.task-created", message.BasicProperties.Type);
        Assert.Equal(row.MessageId.ToString("D"), Encoding.UTF8.GetString((byte[])message.BasicProperties.Headers!["templateapp-message-id"]!));
        Assert.Equal("1", Encoding.UTF8.GetString((byte[])message.BasicProperties.Headers!["templateapp-message-version"]!));
        Assert.Contains($"\"taskId\":\"{task}\"", Encoding.UTF8.GetString(message.Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithRabbitMqDown_TheRelayKeepsTheEventPending_WithoutSpendingAttempts_AndPublishesItWhenRabbitMqReturns()
    {
        _ownRabbit = RabbitMqFixture.Build();
        await _ownRabbit.StartAsync(TestContext.Current.CancellationToken);
        Uri uri = new(_ownRabbit.GetConnectionString());
        IsolatedApi api = await IsolatedApi.StartAsync(_server, uri);
        _apis.Add(api);
        TestSession alice = await api.SignUpAsync("alice");
        string project = await alice.CreateProjectAsync("Casa");
        string task = await alice.CreateTaskAsync("Comprar pan", project);

        await _ownRabbit.PauseAsync(TestContext.Current.CancellationToken);
        Stopwatch down = Stopwatch.StartNew();
        for (int cycle = 0; cycle < 20; cycle++)
        {
            await api.Relay.RunOnceAsync(TestContext.Current.CancellationToken);
            api.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        down.Stop();
        OutboxRow pending = Assert.Single(await api.Pair.Tasks.Services.OutboxRowsAsync(task));
        Assert.Equal((0, null, null), (pending.Attempts, pending.ProcessedAt, pending.FailedAt));
        Assert.Contains("BrokerUnavailableException", pending.LastError, StringComparison.Ordinal);

        await _ownRabbit.UnpauseAsync(TestContext.Current.CancellationToken);
        api.Clock.Advance(TimeSpan.FromMinutes(1));
        OutboxRow published = await Eventually.Async(
            async () =>
            {
                await api.Relay.RunOnceAsync(TestContext.Current.CancellationToken);
                api.Clock.Advance(TimeSpan.FromMinutes(1));
                return Assert.Single(await api.Pair.Tasks.Services.OutboxRowsAsync(task));
            },
            row => row.ProcessedAt is not null,
            Wait);
        Assert.Equal(0, published.Attempts);
        await api.DrainOutboxAsync();
        Assert.Equal((1, 0), await api.Pair.Monolith.Services.TaskCountersAsync(project));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
        if (_ownRabbit is not null)
            await _ownRabbit.DisposeAsync();
    }
}
