using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging.Outbox;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class OutboxRelayTests : IAsyncDisposable
{
    private const string Meter = "TemplateApp.Outbox";

    private readonly string _server;
    private readonly List<ApiPair> _apis = [];

    public OutboxRelayTests(PostgresFixture postgres)
    {
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task AFailingConsumer_LeavesTheEventPending_AndKeepsTheChange()
    {
        var clock = NewClock();
        var probe = new ConsumerProbe { Fails = true };
        ApiPair api = await StartAsync(await NewDatabaseAsync(), probe, clock);
        using var failures = new RecordedMeasurements(api.Tasks.Services, Meter, "templateapp.outbox.delivery.failures");
        using var lag = new RecordedMeasurements(api.Tasks.Services, Meter, "templateapp.outbox.delivery.lag");
        var alice = await SignUpAsync(api);
        string id = await alice.CreateTaskAsync("Comprar pan");
        OutboxRelay relay = RelayOf(api);

        await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        var fetched = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        fetched.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        OutboxRow pending = Assert.Single(await api.Tasks.Services.OutboxRowsAsync(id));
        Assert.Equal((1, null, null), (pending.Attempts, pending.ProcessedAt, pending.FailedAt));
        Assert.Contains("The test consumer is down.", pending.LastError, StringComparison.Ordinal);
        Assert.Equal(clock.GetUtcNow().AddSeconds(2), pending.NextAttemptAt);
        RecordedMeasurement failure = Assert.Single(failures.Values);
        Assert.Equal("tasks.task-created", failure.Tags["templateapp.outbox.type"]);

        probe.Fails = false;
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Single(probe.Calls);

        clock.Advance(TimeSpan.FromSeconds(3));
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        OutboxRow processed = Assert.Single(await api.Tasks.Services.OutboxRowsAsync(id));
        Assert.Equal(clock.GetUtcNow(), processed.ProcessedAt);
        Assert.Equal(2, probe.Calls.Count);
        Assert.Equal(1, await EffectsAsync(api));
        Assert.Equal(3, Assert.Single(lag.Values).Value);
    }

    [Fact]
    public async Task TheSameMessageDeliveredTwice_IsConsumedOnce()
    {
        var probe = new ConsumerProbe();
        ApiPair api = await StartAsync(await NewDatabaseAsync(), probe, configure: FailOnceAfterTheConsumer);
        var alice = await SignUpAsync(api);
        string id = await alice.CreateTaskAsync("Comprar pan");
        OutboxRelay relay = RelayOf(api);

        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Null(Assert.Single(await api.Tasks.Services.OutboxRowsAsync(id)).ProcessedAt);
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        OutboxRow row = Assert.Single(await api.Tasks.Services.OutboxRowsAsync(id));
        Assert.NotNull(row.ProcessedAt);
        Assert.Equal([row.MessageId, row.MessageId], probe.Calls);
        Assert.Equal(1, await EffectsAsync(api));
        Assert.Equal(1, await api.Tasks.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."InboxMessages" """));
    }

    [Fact]
    public async Task TheSameMessageDeliveredTwice_WithoutTheInbox_RepeatsTheEffect()
    {
        var probe = new ConsumerProbe { UsesInbox = false };
        ApiPair api = await StartAsync(await NewDatabaseAsync(), probe, configure: FailOnceAfterTheConsumer);
        var alice = await SignUpAsync(api);
        await alice.CreateTaskAsync("Comprar pan");
        OutboxRelay relay = RelayOf(api);

        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.RunOnceAsync(TestContext.Current.CancellationToken));
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, probe.Calls.Count);
        Assert.Equal(2, await EffectsAsync(api));
    }

    [Fact]
    public async Task TwoRelaysAtOnce_DeliverEachMessageOnce()
    {
        var probe = new ConsumerProbe();
        string database = await NewDatabaseAsync();
        var settings = new Dictionary<string, string> { ["Outbox:BatchSize"] = "5" };
        ApiPair first = await StartAsync(database, probe, settings: settings);
        ApiPair second = await StartAsync(database, probe, settings: settings);
        var alice = await SignUpAsync(first);
        for (int i = 0; i < 20; i++)
            await alice.CreateTaskAsync($"Tarea {i}");

        await Task.WhenAll(DrainAsync(RelayOf(first)), DrainAsync(RelayOf(second)));

        Assert.Equal(20, probe.Calls.Count);
        Assert.Equal(20, probe.Calls.Distinct().Count());
        Assert.All(await first.Tasks.Services.AllOutboxRowsAsync(), row => Assert.NotNull(row.ProcessedAt));
        Assert.Equal(20, await EffectsAsync(first));
    }

    [Fact]
    public async Task AChangeThatRollsBack_PublishesNothing()
    {
        var probe = new ConsumerProbe();
        ApiPair api = await StartAsync(
            await NewDatabaseAsync(),
            probe,
            configure: services => services.ConfigureDbContext<TasksDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<TaskItem>())));
        var alice = await SignUpAsync(api);

        var failed = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/tasks",
            new { title = "Comprar pan" },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        failed.Payload?.Dispose();
        await RelayOf(api).RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal(0L, (await api.Tasks.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
        Assert.Empty(await api.Tasks.Services.AllOutboxRowsAsync());
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public async Task AfterMaxAttempts_TheMessageFails_AndIsNotTakenAgain()
    {
        var clock = NewClock();
        var probe = new ConsumerProbe { Fails = true };
        ApiPair api = await StartAsync(
            await NewDatabaseAsync(),
            probe,
            clock,
            new Dictionary<string, string> { ["Outbox:MaxAttempts"] = "2" });
        using var failedGauge = new RecordedMeasurements(api.Tasks.Services, Meter, "templateapp.outbox.failed");
        var alice = await SignUpAsync(api);
        string id = await alice.CreateTaskAsync("Comprar pan");
        OutboxRelay relay = RelayOf(api);

        await relay.RunOnceAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(3));
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);
        DateTimeOffset failedAt = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        OutboxRow row = Assert.Single(await api.Tasks.Services.OutboxRowsAsync(id));
        Assert.Equal((2, failedAt, null, null), (row.Attempts, row.FailedAt, row.ProcessedAt, row.NextAttemptAt));
        Assert.Equal(2, probe.Calls.Count);
        Assert.Equal(1, failedGauge.Values[^1].Value);
    }

    [Fact]
    public async Task ProcessedMessages_AreDeletedAfterTheRetention()
    {
        var clock = NewClock();
        var probe = new ConsumerProbe();
        ApiPair api = await StartAsync(await NewDatabaseAsync(), probe, clock);
        var alice = await SignUpAsync(api);
        await alice.CreateTaskAsync("Comprar pan");
        OutboxRelay relay = RelayOf(api);

        await relay.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Single(await api.Tasks.Services.AllOutboxRowsAsync());
        clock.Advance(TimeSpan.FromDays(8));
        await relay.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await api.Tasks.Services.AllOutboxRowsAsync());
    }

    [Fact]
    public async Task TheConsumerSpan_LinksToTheRequestThatSavedTheEvent()
    {
        var probe = new ConsumerProbe();
        ApiPair api = await StartAsync(await NewDatabaseAsync(), probe);
        var alice = await SignUpAsync(api);
        string traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OutboxTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var created = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/tasks",
            new { title = "Comprar pan" },
            headers: new Dictionary<string, string> { ["traceparent"] = $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01" });
        created.Payload?.Dispose();
        await RelayOf(api).RunOnceAsync(TestContext.Current.CancellationToken);

        Activity span = Assert.Single(spans, s => s.Links.Any(link => link.Context.TraceId.ToHexString() == traceId));
        Assert.Equal((ActivityKind.Consumer, "outbox process tasks.task-created"), (span.Kind, span.DisplayName));
        Assert.NotEqual(traceId, span.TraceId.ToHexString());
    }

    public ValueTask DisposeAsync()
    {
        foreach (ApiPair api in _apis)
            api.Dispose();

        return ValueTask.CompletedTask;
    }

    private static FakeTimeProvider NewClock() =>
        new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    private static void FailOnceAfterTheConsumer(IServiceCollection services)
    {
        FailOnceMarkingProcessed failure = new();
        services.ConfigureDbContext<TasksDbContext>((_, options) => options.AddInterceptors(failure));
    }

    private static OutboxRelay RelayOf(ApiPair api) =>
        api.Tasks.Services.GetRequiredKeyedService<OutboxRelay>(typeof(TasksDbContext));

    private static async Task DrainAsync(OutboxRelay relay)
    {
        while (await relay.RunOnceAsync(TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    private static Task<long> EffectsAsync(ApiPair api) =>
        api.Tasks.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."OutboxTestEffects" """);

    private static async Task<TestSession> SignUpAsync(ApiPair api)
    {
        var alice = new TestSession(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        return alice;
    }

    private Task<string> NewDatabaseAsync() => TestDatabases.CreateEmptyAsync(_server);

    private async Task<ApiPair> StartAsync(
        string database,
        ConsumerProbe probe,
        TimeProvider? clock = null,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? configure = null)
    {
        ApiPair api = new(database, settings ?? new Dictionary<string, string>(), services =>
        {
            services.AddSingleton(probe);
            services.AddEventHandler<TaskCreated, RecordingConsumer>();
            if (clock is not null)
                services.AddSingleton(clock);
            configure?.Invoke(services);
        });
        _apis.Add(api);
        api.Start();

        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand create = new(
            """CREATE TABLE IF NOT EXISTS tasks."OutboxTestEffects" ("MessageId" uuid NOT NULL, "TaskId" uuid NOT NULL)""",
            connection);
        await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return api;
    }
}
