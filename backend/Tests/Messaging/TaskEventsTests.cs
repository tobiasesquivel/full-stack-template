using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.DeleteOwnedTasks;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class TaskEventsTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public TaskEventsTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_SavesItsEventInTheOutbox_WithTheTraceOfTheRequest()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string traceId = ActivityTraceId.CreateRandom().ToHexString();
        var headers = new Dictionary<string, string> { ["traceparent"] = $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01" };

        var created = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: headers);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        string id;
        using (created.Payload)
            id = created.Payload!.RootElement.GetProperty("id").GetString()!;

        OutboxRow row = Assert.Single(await _fixture.TasksServices.OutboxRowsAsync(id));
        Assert.Equal(("tasks.task-created", 1, 0), (row.Type, row.Version, row.Attempts));
        Assert.Equal((id, alice.AuthId), (row.Event.GetProperty("taskId").GetString(), row.Event.GetProperty("ownerId").GetString()));
        Assert.Null(row.ProcessedAt);
        Assert.NotEqual(Guid.Empty, row.MessageId);
        Assert.Matches($"^00-{traceId}-[0-9a-f]{{16}}-0[01]$", row.TraceParent);
    }

    [Fact]
    public async Task CompleteReopenAndDelete_EachSaveTheirEvent()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Comprar pan");

        await PutAsync(alice, id, "Comprar pan", isCompleted: true);
        await PutAsync(alice, id, "Comprar pan", isCompleted: false);
        var deleted = await alice.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        List<OutboxRow> rows = await _fixture.TasksServices.OutboxRowsAsync(id);
        Assert.Equal(
            ["tasks.task-created", "tasks.task-completed", "tasks.task-reopened", "tasks.task-deleted"],
            rows.Select(r => r.Type));
        Assert.False(rows[^1].Event.GetProperty("wasCompleted").GetBoolean());
    }

    [Fact]
    public async Task DeletingTheTasksOfAnOwner_SavesOneEventPerTask()
    {
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        string done = await bob.CreateTaskAsync("Hecha");
        string open = await bob.CreateTaskAsync("Abierta");
        await PutAsync(bob, done, "Hecha", isCompleted: true);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (AsyncServiceScope scope = _fixture.TasksServices.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DeleteOwnedTasks>().ExecuteAsync(
                bob.AuthId, now, new EventContext(Guid.NewGuid(), OwnerTombstones.Consumer, now, now), TestContext.Current.CancellationToken);

        OutboxRow doneDeleted = (await _fixture.TasksServices.OutboxRowsAsync(done)).Last();
        OutboxRow openDeleted = (await _fixture.TasksServices.OutboxRowsAsync(open)).Last();
        Assert.Equal(("tasks.task-deleted", true), (doneDeleted.Type, doneDeleted.Event.GetProperty("wasCompleted").GetBoolean()));
        Assert.Equal(("tasks.task-deleted", false), (openDeleted.Type, openDeleted.Event.GetProperty("wasCompleted").GetBoolean()));
    }

    [Fact]
    public async Task AStaleUpdateAndAnIdempotentReplay_SaveNoEvent()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var key = new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() };
        var first = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Una vez" }, headers: key);
        var replay = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Una vez" }, headers: key);
        string id;
        using (first.Payload)
            id = first.Payload!.RootElement.GetProperty("id").GetString()!;
        replay.Payload?.Dispose();
        Assert.Equal("true", replay.Headers["Idempotent-Replayed"]);

        uint version = await alice.TaskVersionAsync(id);
        await PutAsync(alice, id, "Una vez", isCompleted: true);
        var stale = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Otra", isCompleted = false, version });
        stale.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);

        Assert.Equal(
            ["tasks.task-created", "tasks.task-completed"],
            (await _fixture.TasksServices.OutboxRowsAsync(id)).Select(r => r.Type));
    }

    private static async Task PutAsync(TestSession session, string id, string title, bool isCompleted)
    {
        uint version = await session.TaskVersionAsync(id);
        var updated = await session.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title, isCompleted, version });
        updated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, updated.Status);
    }
}
