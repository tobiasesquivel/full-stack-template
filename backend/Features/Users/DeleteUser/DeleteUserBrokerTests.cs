using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.DeleteUser;

public sealed class DeleteUserBrokerTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public DeleteUserBrokerTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task DeleteMe_LeavesNoRowOfTheAccountInAnyFeature_OnceTasksConsumesTheEvent()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        await alice.CreateTaskAsync("Sin clave");
        await alice.CreateProjectAsync("Mudanza");
        SessionResponse keyed = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/tasks",
            new { title = "Con clave" },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        keyed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, keyed.Status);
        Assert.Equal(new OwnedRows(1, 1, 1, 1, 2, 1, 1), await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId));

        SessionResponse deleted = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        deleted.Payload?.Dispose();
        await api.DrainUsersOutboxAsync();

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId));
    }

    [Fact]
    public async Task WithTasksDown_DeleteMe_Answers204_AndTheTasksGoWhenTasksComesBack()
    {
        RemoteFault tasks = RemoteFault.Unreachable(RemoteFault.Host(ApiPair.TasksAuthority));
        tasks.Active = false;
        IsolatedApi api = await StartAsync(tasks);
        TestSession alice = await api.SignUpAsync("alice");
        string done = await alice.CreateTaskAsync("Hecha");
        string open = await alice.CreateTaskAsync("Abierta");
        await TaskCounterScenario.SetCompletedAsync(alice, done, true);
        await TasksListener.StopAsync(api);
        tasks.Active = true;

        SessionResponse deleted = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        deleted.Payload?.Dispose();
        OwnedRows whileDown = await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId);
        await api.PublishUsersOutboxAsync();
        long tasksBeforeTheReturn = (await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId)).Tasks;
        await TasksListener.StartAsync(api);
        await api.DrainUsersOutboxAsync();

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        Assert.Equal(new OwnedRows(0, 0, 0, 0, 2, 0, 0), whileDown);
        Assert.Equal(2, tasksBeforeTheReturn);
        Assert.Equal(0, tasks.Calls);
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(alice.AuthId));
        Assert.Equal(1, await DeletedEventsAsync(api, done));
        Assert.Equal(1, await DeletedEventsAsync(api, open));
        AuditRow taskDeletion = (await api.Pair.Monolith.Services.AuditEntriesAsync(open)).Last();
        Assert.Equal(("Deleted", "system", null), (taskDeletion.Operation, taskDeletion.ActorKind, taskDeletion.ActorId));
        Assert.Equal(1, await api.Pair.Monolith.Services.CountAsync($"""SELECT count(*) AS "Value" FROM tasks."DeletedOwners" WHERE "OwnerId" = {alice.AuthId}"""));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static Task<long> DeletedEventsAsync(IsolatedApi api, string taskId) =>
        api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM tasks."OutboxMessages" WHERE "Type" = 'tasks.task-deleted' AND "Payload"->>'taskId' = {taskId}""");

    private async Task<IsolatedApi> StartAsync(RemoteFault? fault = null)
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker, interceptRemote: fault is null ? null : fault.Intercept);
        _apis.Add(api);
        return api;
    }
}
