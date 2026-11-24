using Npgsql;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

public sealed class UserDeletionsTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public UserDeletionsTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task AUserDeletedEvent_DeletesOnlyThatOwnersTasks_WithOneEventEach_AndBuriesTheOwner()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        TestSession bob = await api.SignUpAsync("bob");
        string done = await alice.CreateTaskAsync("Hecha");
        string open = await alice.CreateTaskAsync("Abierta");
        await TaskCounterScenario.SetCompletedAsync(alice, done, true);
        await bob.CreateTaskAsync("De Bob");

        await broker.PublishUserDeletedAsync(Guid.NewGuid(), alice.AuthId);
        await ConsumedAsync(api, broker);

        Assert.Equal(0, (await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
        Assert.Equal(1, (await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId)).Tasks);
        Assert.Equal(1, await DeletedEventsAsync(api, done));
        Assert.Equal(1, await DeletedEventsAsync(api, open));
        Assert.Equal(1, await TombstonesAsync(api, alice.AuthId));
        Assert.Equal(0, await TombstonesAsync(api, bob.AuthId));
    }

    [Fact]
    public async Task TheSameEventTwice_DeletesOnce()
    {
        (IsolatedApi api, TaskEventsBroker broker) = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string first = await alice.CreateTaskAsync("Antes de la primera entrega");
        Guid message = Guid.NewGuid();

        await broker.PublishUserDeletedAsync(message, alice.AuthId);
        Assert.Equal(1, await Eventually.Async(() => InboxRowsAsync(api, message), rows => rows == 1, Wait));
        Assert.Equal(1, await DeletedEventsAsync(api, first));
        Guid seeded = await SeedTaskAsync(api, alice.AuthId);
        await broker.PublishUserDeletedAsync(message, alice.AuthId);
        await ConsumedAsync(api, broker);

        Assert.Equal(1, await DeletedEventsAsync(api, first));
        Assert.Equal(1, await InboxRowsAsync(api, message));
        Assert.Equal(1, (await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
        Assert.Equal(0, await DeletedEventsAsync(api, seeded.ToString()));
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
        await broker.PublishUserDeletedAsync(marker, $"marker-{marker:N}");
        Assert.Equal(1, await Eventually.Async(() => InboxRowsAsync(api, marker), rows => rows == 1, Wait));
    }

    private static async Task<Guid> SeedTaskAsync(IsolatedApi api, string ownerId)
    {
        Guid id = Guid.NewGuid();
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(api.Database);
        await using NpgsqlCommand insert = new(
            """INSERT INTO tasks."TaskItems" ("Id", "OwnerId", "Title", "CreatedAt") VALUES ($1, $2, 'Entre entregas', now())""",
            connection);
        insert.Parameters.Add(new NpgsqlParameter { Value = id });
        insert.Parameters.Add(new NpgsqlParameter { Value = ownerId });
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static Task<long> InboxRowsAsync(IsolatedApi api, Guid messageId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."InboxMessages" WHERE "MessageId" = {messageId} AND "Consumer" = {OwnerTombstones.Consumer}""");

    private static Task<long> DeletedEventsAsync(IsolatedApi api, string taskId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."OutboxMessages" WHERE "Type" = 'tasks.task-deleted' AND "Payload"->>'taskId' = {taskId}""");

    private static Task<long> TombstonesAsync(IsolatedApi api, string ownerId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."DeletedOwners" WHERE "OwnerId" = {ownerId}""");

    private async Task<(IsolatedApi Api, TaskEventsBroker Broker)> StartAsync()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri);
        _apis.Add(api);
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return (api, broker);
    }
}
