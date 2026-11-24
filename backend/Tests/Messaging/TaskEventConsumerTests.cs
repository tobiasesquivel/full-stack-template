using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using TemplateApp.Features.Projects.ApplyTaskEvent;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class TaskEventConsumerTests : IAsyncDisposable
{
    private const string Monolith = "TemplateApp";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public TaskEventConsumerTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Theory]
    [InlineData("99")]
    [InlineData("2147483648")]
    public async Task AnUnreadableEvent_GoesToTheDeadLetterQueue_AtTheFirstAttempt(string version)
    {
        ConsumerScript script = new();
        CapturedLogs logs = new();
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync(script, services => services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs])));
        string project = await ProjectAsync(api);

        await broker.PublishAsync("tasks.task-created", Guid.NewGuid(), new { projectId = project }, version);

        uint deadLettered = await Eventually.Async(() => broker.CountAsync(TaskEventsBroker.DeadLetterQueue), count => count > 0, Wait);
        Assert.Equal(1u, deadLettered);
        BasicGetResult dead = (await broker.TakeAsync(TaskEventsBroker.DeadLetterQueue))!;
        Assert.Equal("rejected", DeathReason(dead));
        Assert.Equal(1, logs.Entries.Count(entry =>
            entry.Exception is UnreadableMessageException && entry.Message.StartsWith("Failed to process message", StringComparison.Ordinal)));
        Assert.Empty(script.Calls);
        Assert.Equal((0, 0), await api.Pair.Monolith.Services.TaskCountersAsync(project));
    }

    [Fact]
    public async Task AConsumerKilledAfterItsEffect_GetsTheMessageAgain_AndCountsOnce()
    {
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsumerScript script = new() { HoldAfterTheEffect = hold };
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync(script);
        string project = await ProjectAsync(api);
        Guid message = Guid.NewGuid();

        await broker.PublishAsync("tasks.task-created", message, new { projectId = project });
        await script.EffectApplied.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal((1, 0), await api.Pair.Monolith.Services.TaskCountersAsync(project));
        Assert.True(await Eventually.Async(() => broker.CloseConnectionsAsync(Monolith), closed => closed > 0, Wait) > 0);
        hold.SetResult();

        await Eventually.Async(() => Task.FromResult(script.Calls.Count), calls => calls >= 2, Wait);
        Assert.Equal([message, message], script.Calls.Select(call => call.MessageId));
        Assert.Equal((1, 0), await api.Pair.Monolith.Services.TaskCountersAsync(project));
        Assert.Equal(1, await InboxRowsAsync(api));
    }

    [Fact]
    public async Task TwoMonoliths_OneConsumesTheQueue_InPublishingOrder()
    {
        ConsumerScript script = new();
        (IsolatedApi first, TaskEventsBroker broker) = await StartAsync(script, host: "first");
        IsolatedApi second = IsolatedApi.StartOn(first.Database, _rabbit.Uri, first.Prefix, services => Script(services, script, "second"));
        _apis.Add(second);
        string project = await ProjectAsync(first);
        Assert.Equal(2, await Eventually.Async(() => broker.ConsumersAsync(TaskEventsBroker.Queue), consumers => consumers == 2, Wait));

        List<Guid> published = [.. Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())];
        foreach (Guid message in published)
            await broker.PublishAsync("tasks.task-created", message, new { projectId = project });

        Assert.Equal((100, 0), await CountersWhenAsync(first, project, (100, 0)));
        Assert.Single(script.Calls.Select(call => call.Host).Distinct());
        Assert.Equal(published, script.Calls.Select(call => call.MessageId));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    internal static void Script(IServiceCollection services, ConsumerScript script, string host) =>
        services.AddScoped<IIntegrationEventHandler<TaskCreatedIntegrationEvent>>(provider =>
            new ScriptedTaskCreatedConsumer(script, host, new TaskEventsConsumer(provider.GetRequiredService<ApplyTaskEvent>())));

    private static string DeathReason(BasicGetResult dead)
    {
        List<object> deaths = (List<object>)dead.BasicProperties.Headers!["x-death"]!;
        Dictionary<string, object> death = (Dictionary<string, object>)deaths[0];
        return Encoding.UTF8.GetString((byte[])death["reason"]);
    }

    internal static async Task<string> ProjectAsync(IsolatedApi api)
    {
        TestSession alice = await api.SignUpAsync("alice");
        return await alice.CreateProjectAsync("Casa");
    }

    internal static async Task<(int Tasks, int Completed)?> CountersWhenAsync(IsolatedApi api, string project, (int, int) expected) =>
        await Eventually.Async(() => api.Pair.Monolith.Services.TaskCountersAsync(project), counters => counters == expected, Wait);

    private static Task<long> InboxRowsAsync(IsolatedApi api) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM projects."InboxMessages" """);

    private async Task<(IsolatedApi Api, TaskEventsBroker Broker)> StartAsync(
        ConsumerScript script,
        Action<IServiceCollection>? configure = null,
        string host = "monolith")
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri, services =>
        {
            Script(services, script, host);
            configure?.Invoke(services);
        });
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return (api, broker);
    }
}
