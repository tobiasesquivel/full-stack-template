using System.Net;
using TemplateApp.Features.Tasks.HoldOwnerTasks;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

public sealed class ReleaseOwnerTasksTests : IAsyncDisposable
{
    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public ReleaseOwnerTasksTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task ARejection_ReleasesTheHoldOfItsClosure_AndTheOwnerCreatesTasksAgain()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        Guid closure = Guid.NewGuid();
        Guid requested = Guid.NewGuid();
        Guid rejected = Guid.NewGuid();
        await broker.PublishClosureRequestedAsync(requested, closure, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, requested);

        await broker.PublishClosureRejectedAsync(rejected, closure, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, rejected);

        Assert.Equal(0, await HoldOwnerTasksTests.HoldsAsync(api, alice.AuthId, closure));
        await alice.CreateTaskAsync("Después del rechazo");
    }

    [Fact]
    public async Task AReleaseCommand_ReleasesTheHold_AndRepliesOwnerTasksReleasedOnce()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        Guid closure = Guid.NewGuid();
        Guid held = Guid.NewGuid();
        Guid released = Guid.NewGuid();
        Guid again = Guid.NewGuid();
        await broker.PublishHoldCommandAsync(held, closure, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, held);

        await broker.PublishReleaseCommandAsync(released, closure, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, released);
        await broker.PublishReleaseCommandAsync(again, closure, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, again);

        Assert.Equal(0, await HoldOwnerTasksTests.HoldsAsync(api, alice.AuthId, closure));
        Assert.Equal(1, await ReleasedEventsAsync(api, closure));
        await alice.CreateTaskAsync("Después de soltar");
    }

    [Fact]
    public async Task AnOldRejection_DoesNotReleaseTheHoldOfANewerClosure()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        Guid older = Guid.NewGuid();
        Guid newer = Guid.NewGuid();
        Guid requested = Guid.NewGuid();
        Guid rejected = Guid.NewGuid();
        await broker.PublishClosureRequestedAsync(requested, newer, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, requested);

        await broker.PublishClosureRejectedAsync(rejected, older, alice.AuthId);
        await HoldOwnerTasksTests.AcceptedAsync(api, rejected);

        Assert.Equal(1, await HoldOwnerTasksTests.HoldsAsync(api, alice.AuthId, newer));
        SessionResponse refused = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Sigue en cierre" });
        ProblemAssert.Problem(refused, HttpStatusCode.Conflict, "Task.OwnerClosing");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static Task<long> ReleasedEventsAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."OutboxMessages" WHERE "Type" = 'tasks.owner-tasks-released' AND "Payload"->>'closureId' = {closureId.ToString()}""");

    private async Task<(IsolatedApi Api, TaskEventsBroker Broker)> StartAsync()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return (api, broker);
    }
}
