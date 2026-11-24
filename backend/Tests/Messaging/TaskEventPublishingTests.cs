using System.Text;
using RabbitMQ.Client;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class TaskEventPublishingTests : IAsyncDisposable
{
    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

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

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }
}
