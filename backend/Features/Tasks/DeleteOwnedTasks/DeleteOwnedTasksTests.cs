using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

public sealed class DeleteOwnedTasksTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;
    private readonly string _database;

    public DeleteOwnedTasksTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _database = postgres.ConnectionString;
    }

    [Fact]
    public async Task DeletesOnlyTheOwnersTasks_AndRepeatingItChangesNothing()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        await alice.CreateTaskAsync("De Alice");
        await bob.CreateTaskAsync("De Bob");

        using HttpResponseMessage first = await DeleteAsync(alice.AuthId, actor: null);
        using HttpResponseMessage again = await DeleteAsync(alice.AuthId, actor: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(0, await CountAsync(alice));
        Assert.Equal(1, await CountAsync(bob));
    }

    [Fact]
    public async Task TheAuditNamesTheUserTheCallerActsFor()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Auditada");

        using HttpResponseMessage deleted = await DeleteAsync(alice.AuthId, actor: "admin-42");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        AuditRow row = (await _fixture.Services.AuditEntriesAsync(id)).Last();
        Assert.Equal(("Deleted", "user", "admin-42"), (row.Operation, row.ActorKind, row.ActorId));
    }

    [Fact]
    public async Task ATaskCreatedWhileTheDeletionWaits_IsDeletedToo()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateTaskAsync("Antes");
        Guid late = Guid.NewGuid();
        await using HeldOwnerLock create = await HeldOwnerLock.SharedAsync(_database, alice.AuthId);
        await create.ExecuteAsync(
            """INSERT INTO tasks."TaskItems" ("Id", "OwnerId", "Title", "CreatedAt") VALUES ($1, $2, 'En carrera', now())""",
            late,
            alice.AuthId);

        Task deletion = DeleteByEventAsync(alice.AuthId);
        bool waited = await create.BlocksAsync(deletion);
        await create.CommitAsync();
        await deletion;

        Assert.True(waited, "The deletion did not wait for the task being created.");
        Assert.Equal(0, (await _fixture.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
        Assert.Equal("tasks.task-deleted", (await _fixture.TasksServices.OutboxRowsAsync(late.ToString())).Last().Type);
    }

    private async Task DeleteByEventAsync(string ownerId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using AsyncServiceScope scope = _fixture.TasksServices.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeleteOwnedTasks>()
            .ExecuteAsync(ownerId, now, new EventContext(Guid.NewGuid(), OwnerTombstones.Consumer, now, now), TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> DeleteAsync(string ownerId, string? actor)
    {
        using HttpClient tasks = _fixture.CreateTasksClient();
        using HttpRequestMessage request = new(HttpMethod.Delete, $"internal/v1/owners/{ownerId}/tasks");
        if (actor is not null)
            request.Headers.Add(OnBehalfOf.Header, actor);
        return await tasks.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<int> CountAsync(TestSession session)
    {
        SessionResponse list = await session.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.NotNull(list.Payload);
        using (list.Payload)
            return list.Payload.RootElement.GetProperty("items").GetArrayLength();
    }
}
