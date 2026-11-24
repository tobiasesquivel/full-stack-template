using System.Diagnostics;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Testcontainers.RabbitMq;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class TaskEventPublishingBrokerDownTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly List<IsolatedApi> _apis = [];
    private RabbitMqContainer? _ownRabbit;

    public TaskEventPublishingBrokerDownTests(PostgresFixture postgres)
    {
        _server = postgres.ConnectionString;
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
        foreach (IsolatedApi api in _apis)
            api.Dispose();
        if (_ownRabbit is not null)
            await _ownRabbit.DisposeAsync();
    }
}
