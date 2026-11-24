using System.Net;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Tasks.HoldOwnerTasks;

public sealed class HoldOwnerTasksTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public HoldOwnerTasksTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task AHoldCommand_HoldsOnlyThatOwner_AndRepliesOwnerTasksHeldOnce()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        TestSession bob = await api.SignUpAsync("bob");
        await alice.CreateTaskAsync("Antes del cierre");
        Guid closure = Guid.NewGuid();
        Guid message = Guid.NewGuid();

        await broker.PublishHoldCommandAsync(message, closure, alice.AuthId);
        await AcceptedAsync(api, message);
        await broker.PublishHoldCommandAsync(message, closure, alice.AuthId);
        await AcceptedAsync(api, await MarkerAsync(broker));

        SessionResponse refused = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Durante el cierre" });
        ProblemAssert.Problem(refused, HttpStatusCode.Conflict, "Task.OwnerClosing");
        await bob.CreateTaskAsync("Bob sigue");
        Assert.Equal(1, (await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
        Assert.Equal(1, await HoldsAsync(api, alice.AuthId, closure));
        Assert.Equal(1, await HeldEventsAsync(api, closure));
    }

    [Fact]
    public async Task AUserDeletedEvent_RemovesTheHold_AndLeavesTheTombstone()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        Guid closure = Guid.NewGuid();
        Guid requested = Guid.NewGuid();
        Guid deleted = Guid.NewGuid();

        await broker.PublishHoldCommandAsync(requested, closure, alice.AuthId);
        await AcceptedAsync(api, requested);
        await broker.PublishUserDeletedAsync(deleted, alice.AuthId);
        Assert.Equal(1, await Eventually.Async(() => InboxRowsAsync(api, deleted, "tasks.user-deletions"), rows => rows == 1, Wait));

        Assert.Equal(0, await HoldsAsync(api, alice.AuthId, closure));
        Assert.Equal(1, await api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."DeletedOwners" WHERE "OwnerId" = {alice.AuthId}"""));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    internal static async Task AcceptedAsync(IsolatedApi api, Guid message) =>
        Assert.Equal(1, await Eventually.Async(() => InboxRowsAsync(api, message, "tasks.account-closures"), rows => rows == 1, Wait));

    internal static async Task<Guid> MarkerAsync(TaskEventsBroker broker)
    {
        Guid marker = Guid.NewGuid();
        await broker.PublishHoldCommandAsync(marker, Guid.NewGuid(), $"marker-{marker:N}");
        return marker;
    }

    internal static Task<long> HoldsAsync(IsolatedApi api, string ownerId, Guid closureId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."OwnerHolds" WHERE "OwnerId" = {ownerId} AND "ClosureId" = {closureId}""");

    private static Task<long> InboxRowsAsync(IsolatedApi api, Guid messageId, string consumer) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."InboxMessages" WHERE "MessageId" = {messageId} AND "Consumer" = {consumer}""");

    private static Task<long> HeldEventsAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."OutboxMessages" WHERE "Type" = 'tasks.owner-tasks-held' AND "Payload"->>'closureId' = {closureId.ToString()}""");

    private async Task<(IsolatedApi Api, TaskEventsBroker Broker)> StartAsync()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return (api, broker);
    }
}
