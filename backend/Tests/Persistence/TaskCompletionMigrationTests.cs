using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Infrastructure.Persistence.Initialization;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class TaskCompletionMigrationTests : IClassFixture<ApiFixture>
{
    private const string Owner = "owner-migration";
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Edited = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EditedDuringRollout = new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedByNewRelease = new(2026, 1, 4, 0, 0, 0, TimeSpan.Zero);

    private readonly ApiFixture _fixture;
    private readonly PostgresFixture _postgres;

    public TaskCompletionMigrationTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _postgres = postgres;
    }

    [Fact]
    public async Task ExpandBackfillStopWritingAndContract_KeepEveryCompletion_AndLeaveTheApiReady()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);
        using HttpClient client = ClientWithoutStartupMigration(database, out IServiceProvider services);
        using IServiceScope scope = services.CreateScope();
        IMigrator migrator = scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>();

        await migrator.MigrateAsync("AddTaskListIndex");
        await ExecuteAsync(database, """
            INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@owner, false, false, false, false, 0);
            INSERT INTO "UserProfiles" ("AuthId", "DisplayName") VALUES (@owner, 'Owner');
            INSERT INTO "TaskItems" ("Id", "OwnerId", "Title", "IsCompleted", "CreatedAt", "UpdatedAt") VALUES
                (gen_random_uuid(), @owner, 'completed and edited', true, @created, @edited),
                (gen_random_uuid(), @owner, 'completed never edited', true, @created, NULL),
                (gen_random_uuid(), @owner, 'open', false, @created, @edited);
            """);

        await migrator.MigrateAsync("ExpandTaskCompletedAt");
        await ExecuteAsync(database, """
            INSERT INTO "TaskItems" ("Id", "OwnerId", "Title", "IsCompleted", "CompletedAt", "CreatedAt", "UpdatedAt") VALUES
                (gen_random_uuid(), @owner, 'completed by the old release during the rollout', true, NULL, @created, @editedDuringRollout),
                (gen_random_uuid(), @owner, 'completed by the new release, reopened by the old one', false, @edited, @created, @editedDuringRollout);
            """);

        await migrator.MigrateAsync("BackfillTaskCompletedAt");
        Assert.Equal(0L, await ScalarAsync(database, """
            SELECT count(*) FROM "TaskItems" WHERE "IsCompleted" <> ("CompletedAt" IS NOT NULL)
            """));

        await migrator.MigrateAsync("StopWritingTaskIsCompleted");
        await ExecuteAsync(database, """
            INSERT INTO "TaskItems" ("Id", "OwnerId", "Title", "CompletedAt", "CreatedAt")
            VALUES (gen_random_uuid(), @owner, 'completed by the new release', @completedByNewRelease, @created);
            """);

        await DatabaseInitializer.InitializeAsync(services);

        Assert.Equal(0L, await ScalarAsync(database, """
            SELECT count(*) FROM information_schema.columns WHERE table_name = 'TaskItems' AND column_name = 'IsCompleted'
            """));
        Assert.Equal(
            new (string, DateTimeOffset?)[]
            {
                ("completed and edited", Edited),
                ("completed by the new release", CompletedByNewRelease),
                ("completed by the new release, reopened by the old one", null),
                ("completed by the old release during the rollout", EditedDuringRollout),
                ("completed never edited", Created),
                ("open", null),
            },
            await CompletionsAsync(database));
        using HttpResponseMessage ready = await client.GetAsync("/api/v1/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task RollingBackTheContract_RebuildsIsCompletedFromCompletedAt()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);
        using HttpClient client = ClientWithoutStartupMigration(database, out IServiceProvider services);
        using IServiceScope scope = services.CreateScope();
        IMigrator migrator = scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>();

        await migrator.MigrateAsync();
        await ExecuteAsync(database, """
            INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@owner, false, false, false, false, 0);
            INSERT INTO "UserProfiles" ("AuthId", "DisplayName") VALUES (@owner, 'Owner');
            INSERT INTO "TaskItems" ("Id", "OwnerId", "Title", "CompletedAt", "CreatedAt") VALUES
                (gen_random_uuid(), @owner, 'completed', @edited, @created),
                (gen_random_uuid(), @owner, 'open', NULL, @created);
            """);

        await migrator.MigrateAsync("StopWritingTaskIsCompleted");

        Assert.Equal(0L, await ScalarAsync(database, """
            SELECT count(*) FROM "TaskItems" WHERE "IsCompleted" <> ("CompletedAt" IS NOT NULL)
            """));
        Assert.Equal(1L, await ScalarAsync(database, """SELECT count(*) FROM "TaskItems" WHERE "IsCompleted" """));
    }

    private HttpClient ClientWithoutStartupMigration(string database, out IServiceProvider services) =>
        _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database },
            TestDatabases.SkipInitialization,
            out services);

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand command = WithParameters(new NpgsqlCommand(sql, connection));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand command = new(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(string, DateTimeOffset?)[]> CompletionsAsync(string database)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand command = new("""SELECT "Title", "CompletedAt" FROM "TaskItems" ORDER BY "Title" """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<(string, DateTimeOffset?)> rows = [];
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), await reader.IsDBNullAsync(1) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(1)));
        return [.. rows];
    }

    private static NpgsqlCommand WithParameters(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("owner", Owner);
        command.Parameters.AddWithValue("created", Created);
        command.Parameters.AddWithValue("edited", Edited);
        command.Parameters.AddWithValue("editedDuringRollout", EditedDuringRollout);
        command.Parameters.AddWithValue("completedByNewRelease", CompletedByNewRelease);
        return command;
    }
}
