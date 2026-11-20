using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class RetiredBindingsTests : IAsyncDisposable
{
    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public RetiredBindingsTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task TheBindingTheChoreographyLeftOnAuth_IsRemovedWhenTheContractReleaseStarts()
    {
        IsolatedApi before = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, before.Prefix);
        _brokers.Add(broker);
        await broker.BindLegacyAsync("tasks.task-events", "tasks.owner-tasks-held", "auth.account-closures");
        before.Dispose();

        _apis.Add(IsolatedApi.StartOn(before.Database, _rabbit.Uri, before.Prefix));

        Assert.Equal([$"{broker.Name("auth.commands")}:auth.decide-account-closure"], await broker.BindingKeysAsync("auth.account-closures"));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }
}
