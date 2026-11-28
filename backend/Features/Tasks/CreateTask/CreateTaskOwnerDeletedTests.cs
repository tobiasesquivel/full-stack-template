using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Tasks.CreateTask;

public sealed class CreateTaskOwnerDeletedTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;
    private readonly string _database;

    public CreateTaskOwnerDeletedTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _database = postgres.ConnectionString;
    }

    [Fact]
    public async Task Create_ForAnOwnerWhoseTasksWereAlreadyDeleted_Returns401OwnerDeleted_AndStoresNoTask()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await using (NpgsqlConnection connection = await TestDatabases.OpenAsync(_database))
        await using (NpgsqlCommand bury = new("""INSERT INTO tasks."DeletedOwners" ("OwnerId", "DeletedAt") VALUES ($1, now())""", connection))
        {
            bury.Parameters.Add(new NpgsqlParameter { Value = alice.AuthId });
            await bury.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        SessionResponse rejected = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/tasks",
            new { title = "Tarde" },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });

        ProblemAssert.Problem(rejected, HttpStatusCode.Unauthorized, "Task.OwnerDeleted");
        OwnedRows rows = await _fixture.OwnedRowsAsync(alice.AuthId);
        Assert.Equal((0L, 0L), (rows["tasks.TaskItems"], rows["platform.IdempotencyKeys"]));
    }

    [Fact]
    public async Task Create_WhileTheOwnersTasksAreBeingDeleted_WaitsForTheDeletion_AndReturns401OwnerDeleted()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await using HeldOwnerLock deletion = await HeldOwnerLock.ExclusiveAsync(_database, alice.AuthId);
        await deletion.ExecuteAsync("""INSERT INTO tasks."DeletedOwners" ("OwnerId", "DeletedAt") VALUES ($1, now())""", alice.AuthId);

        Task<SessionResponse> create = alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "En carrera" });
        bool waited = await deletion.BlocksAsync(create);
        await deletion.CommitAsync();
        SessionResponse created = await create;

        Assert.True(waited, $"POST /tasks did not wait for the deletion: it answered {(int)created.Status} while the tombstone was not committed yet.");
        ProblemAssert.Problem(created, HttpStatusCode.Unauthorized, "Task.OwnerDeleted");
        Assert.Equal(0, (await _fixture.OwnedRowsAsync(alice.AuthId))["tasks.TaskItems"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_HoldsTheOwnerLockUntilItSaves_SoADeletionThatArrivesMeanwhileWaitsAndDeletesTheTask(bool withIdempotencyKey)
    {
        PauseBeforeSaving<TaskItem> pause = new();
        HttpClient client = _fixture.CreateIsolatedTasksClient(
            services => services.ConfigureDbContext<TasksDbContext>((_, options) => options.AddInterceptors(pause)),
            out IServiceProvider tasks);
        TestSession alice = new(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        Dictionary<string, string>? headers = withIdempotencyKey
            ? new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() }
            : null;

        Task<SessionResponse> create = alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "En carrera" }, headers: headers);
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Task deletion = DeleteByEventAsync(tasks, alice.AuthId);
        bool waited = await HeldOwnerLock.SomeoneWaitsAsync(_database, alice.AuthId, deletion);
        pause.Release();
        SessionResponse created = await create;
        created.Payload?.Dispose();
        await deletion;

        Assert.True(waited, "The deletion did not wait for the POST that had already passed the tombstone: CreateTask let the owner lock go before saving.");
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(0, (await tasks.OwnedRowsAsync(alice.AuthId))["tasks.TaskItems"]);
    }

    private static async Task DeleteByEventAsync(IServiceProvider tasks, string ownerId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using AsyncServiceScope scope = tasks.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeleteOwnedTasks.DeleteOwnedTasks>()
            .ExecuteAsync(ownerId, now, new EventContext(Guid.NewGuid(), OwnerTombstones.Consumer, now, now), TestContext.Current.CancellationToken);
    }
}
