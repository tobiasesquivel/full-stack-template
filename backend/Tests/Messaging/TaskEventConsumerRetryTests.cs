using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class TaskEventConsumerRetryTests : IAsyncDisposable
{
    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public TaskEventConsumerRetryTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task ATransientFailure_IsRetriedPastTheDefaultAttempts_AndCountsOnce()
    {
        ConsumerScript script = new() { FailuresLeft = 3 };
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync(script);
        string project = await TaskEventConsumerTests.ProjectAsync(api);

        await broker.PublishAsync("tasks.task-created", Guid.NewGuid(), new { projectId = project });

        Assert.Equal((1, 0), await TaskEventConsumerTests.CountersWhenAsync(api, project, (1, 0)));
        Assert.Equal(4, script.Calls.Count);
        Assert.Single(script.Calls.Select(call => call.MessageId).Distinct());
        Assert.Equal(0u, await broker.CountAsync(TaskEventsBroker.DeadLetterQueue));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private async Task<(IsolatedApi Api, TaskEventsBroker Broker)> StartAsync(ConsumerScript script)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri, services => TaskEventConsumerTests.Script(services, script, "monolith"));
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return (api, broker);
    }
}
