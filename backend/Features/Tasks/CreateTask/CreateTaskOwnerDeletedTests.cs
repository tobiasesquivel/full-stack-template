using System.Net;
using Npgsql;
using TemplateApp.Tests.Support;
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
        OwnedRows rows = await _fixture.Services.OwnedRowsAsync(alice.AuthId);
        Assert.Equal((0L, 0L), (rows.Tasks, rows.IdempotencyKeys));
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
        Assert.Equal(0, (await _fixture.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
    }
}
