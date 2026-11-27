using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Tests.Messaging;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Bus;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using TemplateApp.Tests.Support.Rows;
using Xunit;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Features.Auth.Persistence;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public sealed class AccountClosureSagaTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private readonly string _server;
    private readonly RabbitMqFixture _rabbit;
    private readonly List<IsolatedApi> _apis = [];
    private readonly List<TaskEventsBroker> _brokers = [];

    public AccountClosureSagaTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task TheMonolithRestartsMidClosure_AndTheClosureResumesFromItsSavedState()
    {
        IsolatedApi before = await StartAsync();
        TaskEventsBroker broker = await ConnectAsync(before);
        TestSession bob = await before.SignUpAsync("bob");
        await bob.CreateTaskAsync("Embalar");
        await bob.CreateTaskAsync("Despachar");
        await bob.CreateProjectAsync("Mudanza");
        await before.DrainAsync();
        await MonolithListener.StopAsync(before, MonolithListener.Closures);

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(before, bob, "api/v1/users/me");
        Assert.Equal(1, await Eventually.Async(() => DeleteUserBrokerTests.HoldsAsync(before, bob.AuthId), holds => holds == 1, Wait));
        await before.PublishOutboxAsync<TasksDbContext>();
        Assert.Equal(1u, await Eventually.Async(() => broker.CountAsync(MonolithListener.Closures), waiting => waiting == 1, Wait));
        string? midState = await DeleteUserBrokerTests.SagaStateAsync(before, closure);
        before.Dispose();

        IsolatedApi after = Restart(before);
        using RecordedMeasurements finished = new(after.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.finished");
        await after.DrainAsync();

        Assert.Equal("HoldingTasks", midState);
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(after, closure));
        Assert.Equal("Completed", await DeleteUserBrokerTests.DecisionAsync(after, closure));
        Assert.Equal(1, await HeldRepliesAsync(after, closure));
        Assert.Empty((await after.Pair.OwnedRowsAsync(bob.AuthId)).NonZero);
        Assert.Equal(1, await after.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."DeletedOwners" WHERE "OwnerId" = {bob.AuthId}"""));
        Assert.Equal(["completed"], finished.Values.Select(v => (string)v.Tags["templateapp.account_closure.outcome"]!));
    }

    [Fact]
    public async Task ARejection_ReleasesTheTasksFirst_AndOnlyThenPutsTheProfileBackToActive()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        await api.Pair.Monolith.Services.SetRoleAsync(alice.UserName, "SuperAdmin", member: true);

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, alice, "api/v1/users/me");
        Assert.Equal(1, await Eventually.Async(() => DeleteUserBrokerTests.HoldsAsync(api, alice.AuthId), holds => holds == 1, Wait));
        await TasksListener.StopAsync(api);
        await api.PublishOutboxAsync<TasksDbContext>();
        Assert.Equal("Rejected", await Eventually.Async(() => DeleteUserBrokerTests.DecisionAsync(api, closure), decision => decision == "Rejected", Wait));
        Assert.Equal("Deciding", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        await api.PublishOutboxAsync<AuthDbContext>();
        Assert.Equal("ReleasingTasks", await Eventually.Async(() => DeleteUserBrokerTests.SagaStateAsync(api, closure), state => state == "ReleasingTasks", Wait));
        JsonElement whileReleasing = await DeleteUserTests.MeAsync(alice, "api/v1/users/me");
        JsonElement closureWhileReleasing = await ClosureAsync(alice, closure);
        await TasksListener.StartAsync(api);
        await api.DrainAsync();

        Assert.Equal("closing", whileReleasing.GetProperty("accountStatus").GetString());
        Assert.Equal(("releasingTasks", "Auth.LastSuperAdmin"), (closureWhileReleasing.GetProperty("state").GetString(), closureWhileReleasing.GetProperty("reason").GetString()));
        JsonElement me = await DeleteUserTests.MeAsync(alice, "api/v1/users/me");
        Assert.Equal("active", me.GetProperty("accountStatus").GetString());
        Assert.Equal("Rejected", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(0, await DeleteUserBrokerTests.HoldsAsync(api, alice.AuthId));
        JsonElement ended = await ClosureAsync(alice, closure);
        Assert.Equal("rejected", ended.GetProperty("state").GetString());
        Assert.NotEqual(JsonValueKind.Null, ended.GetProperty("finishedAt").ValueKind);
    }

    [Fact]
    public async Task AClosureStuckPastItsDeadline_IsMarkedOverdue_AndStillFinishesWhenTasksComesBack()
    {
        IsolatedApi api = await StartAsync(settings: new() { ["AccountClosure:OverdueAfterSeconds"] = "1" });
        using RecordedMeasurements overdue = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.overdue");
        TestSession bob = await api.SignUpAsync("bob");
        await TasksListener.StopAsync(api);

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        Assert.True(await Eventually.Async(() => OverdueAsync(api, closure), late => late, Wait));
        JsonElement stuck = await ClosureAsync(bob, closure);
        await TasksListener.StartAsync(api);
        await api.DrainAsync();

        Assert.Equal(("holdingTasks", true), (stuck.GetProperty("state").GetString(), stuck.GetProperty("overdue").GetBoolean()));
        Assert.Equal(["HoldingTasks"], overdue.Values.Select(v => (string)v.Tags["templateapp.account_closure.state"]!));
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.True(await OverdueAsync(api, closure));
    }

    [Fact]
    public async Task AReplyThatArrivesOnAnotherQueue_IsHandledByTheHandlerOfItsType()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await ConnectAsync(api);
        TestSession bob = await api.SignUpAsync("bob");
        await TasksListener.StopAsync(api);
        await MonolithListener.StopAsync(api, MonolithListener.Closures);
        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        await broker.BindLegacyAsync("tasks.task-events", "tasks.owner-tasks-held", "auth.account-closures");

        await broker.PublishAsync("tasks.owner-tasks-held", Guid.NewGuid(), new { closureId = closure, userId = bob.AuthId, heldAt = DateTimeOffset.UtcNow });
        string? state = await Eventually.Async(() => DeleteUserBrokerTests.SagaStateAsync(api, closure), current => current != "HoldingTasks", Wait);
        await TasksListener.StartAsync(api);
        await MonolithListener.StartAsync(api, MonolithListener.Closures);
        await api.DrainAsync();

        Assert.NotEqual("HoldingTasks", state);
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
    }

    [Fact]
    public async Task ADecisionThatArrivesBeforeTheHoldReply_StillCompletesTheClosure()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await ConnectAsync(api);
        TestSession bob = await api.SignUpAsync("bob");
        await TasksListener.StopAsync(api);
        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        Guid decision = Guid.NewGuid();

        await broker.PublishAsync("auth.account-events", "auth.account-deleted", decision, new { closureId = closure, userId = bob.AuthId, deletedAt = DateTimeOffset.UtcNow });
        Assert.Equal("Completed", await Eventually.Async(() => DeleteUserBrokerTests.SagaStateAsync(api, closure), state => state == "Completed", Wait));
        await TasksListener.StartAsync(api);
        await api.DrainAsync();

        Assert.Equal(0, (await api.Pair.OwnedRowsAsync(bob.AuthId))["users.UserProfiles"]);
        Assert.Null(await DeleteUserBrokerTests.DecisionAsync(api, closure));
    }

    [Fact]
    public async Task AReplyForAFinishedClosure_IsIgnored_AndSendsNoSecondCommand()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await ConnectAsync(api);
        TestSession bob = await api.SignUpAsync("bob");
        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        await api.DrainAsync();
        Guid duplicate = Guid.NewGuid();

        await broker.PublishAsync("tasks.owner-tasks-held", duplicate, new { closureId = closure, userId = bob.AuthId, heldAt = DateTimeOffset.UtcNow });
        Assert.Equal(1, await Eventually.Async(() => HandledAsync(api, duplicate), rows => rows == 1, Wait));
        await api.DrainAsync();

        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(1, await api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM auth."InboxMessages" WHERE "Consumer" = 'auth.account-closures'"""));
    }

    [Fact]
    public async Task AReplyForAClosureTheOrchestratorNeverOpened_IsIgnored_AndDecidesNothing()
    {
        IsolatedApi api = await StartAsync();
        TaskEventsBroker broker = await ConnectAsync(api);
        TestSession bob = await api.SignUpAsync("bob");
        Guid closure = Guid.CreateVersion7();
        Guid message = Guid.NewGuid();

        await broker.PublishAsync("tasks.owner-tasks-held", message, new { closureId = closure, userId = bob.AuthId, heldAt = DateTimeOffset.UtcNow });
        Assert.Equal(1, await Eventually.Async(() => HandledAsync(api, message), rows => rows == 1, Wait));
        await api.DrainAsync();

        Assert.Null(await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Null(await DeleteUserBrokerTests.DecisionAsync(api, closure));
        Assert.Equal(1, (await api.Pair.OwnedRowsAsync(bob.AuthId))["auth.AspNetUsers"]);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TaskEventsBroker broker in _brokers)
            await broker.DisposeAsync();
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    internal static async Task<JsonElement> ClosureAsync(TestSession session, Guid closureId)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{closureId}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
            return response.Payload.RootElement.Clone();
    }

    private static async Task<bool> OverdueAsync(IsolatedApi api, Guid closureId) =>
        await api.Pair.Monolith.Services.TextAsync($"""SELECT "Overdue"::text AS "Value" FROM users."AccountClosureSagas" WHERE "ClosureId" = {closureId}""") == "true";

    private static Task<long> HeldRepliesAsync(IsolatedApi api, Guid closureId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."OutboxMessages" WHERE "Type" = 'tasks.owner-tasks-held' AND "Payload"->>'closureId' = {closureId.ToString()}""");

    private static Task<long> HandledAsync(IsolatedApi api, Guid messageId) =>
        api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM wolverine.wolverine_incoming_envelopes WHERE id = {messageId} AND status = 'Handled'""");

    private async Task<IsolatedApi> StartAsync(Dictionary<string, string>? settings = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _rabbit.Uri, settings: settings);
        _apis.Add(api);
        return api;
    }

    private IsolatedApi Restart(IsolatedApi before)
    {
        _apis.Remove(before);
        IsolatedApi after = IsolatedApi.StartOn(before.Database, _rabbit.Uri, before.Prefix);
        _apis.Add(after);
        return after;
    }

    private async Task<TaskEventsBroker> ConnectAsync(IsolatedApi api)
    {
        TaskEventsBroker broker = await TaskEventsBroker.ConnectAsync(_rabbit, api.Prefix);
        _brokers.Add(broker);
        return broker;
    }
}
