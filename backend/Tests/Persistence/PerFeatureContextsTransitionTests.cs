using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class PerFeatureContextsTransitionTests : IClassFixture<ApiFixture>
{
    private const string MainRelease = "20261117144432_ContractTaskIsCompleted";
    private const string Owner = "owner-transition";
    private static readonly (string Schema, string[] Migrations)[] ContextMigrations =
    [
        ("auth", ["_InitialCreate", "_MoveAuthToSchema"]),
        ("users", ["_InitialCreate", "_MoveUsersToSchema"]),
        ("tasks", ["_InitialCreate", "_MoveTasksToSchema", "_AddTaskProjectId", "_AddTaskProjectIdIndex"]),
        ("platform", ["_InitialCreate", "_MovePlatformToSchema"]),
        ("projects", ["_InitialCreate"]),
    ];
    private static readonly string[] ColumnsAddedAfterTheMove = ["ProjectId"];
    private static readonly string[] DataTables =
        ["AspNetUsers", "AspNetUserRoles", "Sessions", "UserProfiles", "TaskItems", "IdempotencyKeys", "AuditEntries"];
    private static readonly string[] TablesBornInTheirFeatureSchema = ["projects.Projects"];
    private static readonly string[] TablesInTheirFeatureSchema =
    [
        "auth.AspNetRoleClaims",
        "auth.AspNetRoles",
        "auth.AspNetUserClaims",
        "auth.AspNetUserLogins",
        "auth.AspNetUserRoles",
        "auth.AspNetUserTokens",
        "auth.AspNetUsers",
        "auth.Sessions",
        "platform.AuditEntries",
        "platform.IdempotencyKeys",
        "projects.Projects",
        "tasks.TaskItems",
        "users.UserProfiles",
    ];

    private readonly ApiFixture _fixture;
    private readonly PostgresFixture _postgres;

    public PerFeatureContextsTransitionTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _postgres = postgres;
    }

    [Fact]
    public async Task InheritedDatabase_KeepsEveryRow_BaselinesOneHistoryPerContext_AndIsReady()
    {
        string database = await InheritedDatabaseAsync();
        await ExecuteAsync(database, """
            INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName") VALUES ('role-transition', 'User', 'USER');
            INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@owner, 'transition', 'TRANSITION', false, false, false, true, 0);
            INSERT INTO "AspNetUserRoles" ("UserId", "RoleId") VALUES (@owner, 'role-transition');
            INSERT INTO "Sessions" ("Id", "UserId", "CreatedAt", "ExpiresAt", "LastSeenAt")
            VALUES ('6f1f7f0e-0000-4000-8000-000000000001', @owner, '2026-01-01Z', '2026-02-01Z', '2026-01-01Z');
            INSERT INTO "UserProfiles" ("AuthId", "DisplayName") VALUES (@owner, 'Owner');
            INSERT INTO "TaskItems" ("Id", "OwnerId", "Title", "CompletedAt", "CreatedAt") VALUES
                ('6f1f7f0e-0000-4000-8000-000000000002', @owner, 'open', NULL, '2026-01-01Z'),
                ('6f1f7f0e-0000-4000-8000-000000000003', @owner, 'completed', '2026-01-02Z', '2026-01-01Z');
            INSERT INTO "IdempotencyKeys" ("OwnerId", "Key", "Fingerprint", "CreatedAt", "StatusCode", "Body")
            VALUES (@owner, '6f1f7f0e-0000-4000-8000-000000000004', '\x01', '2026-01-01Z', 201, '\x7b7d');
            INSERT INTO "AuditEntries" ("OccurredAt", "ActorKind", "ActorId", "EntityType", "EntityId", "Operation", "ChangedProperties")
            VALUES ('2026-01-01Z', 'user', @owner, 'TaskItem', '6f1f7f0e-0000-4000-8000-000000000002', 'Added', '{}');
            """);
        Dictionary<string, string[]> before = await RowsAsync(database);

        using HttpClient client = StartedClient(database, out IServiceProvider services);

        await AssertReadyAsync(client);
        Dictionary<string, string[]> after = await RowsAsync(database);
        foreach (string table in DataTables)
            Assert.Equal(before[table], after[table]);
        await AssertBaselinedAndMovedAsync(database);
        Assert.Contains(MainRelease, await HistoryAsync(database, "public"));
        await AssertAProjectCanBeCreatedAsync(services);
    }

    [Fact]
    public async Task InheritedDatabase_WhenTasksStartsFirst_TasksWaitsForThePlatformTables_AndBothEndReady()
    {
        string database = await InheritedDatabaseAsync();
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database },
            _ => { },
            start: false);
        using HttpClient tasks = api.CreateTasksClient();
        using HttpClient monolith = api.CreateMonolithClient();

        _ = api.Tasks.Services;
        using (HttpResponseMessage waiting = await tasks.GetAsync("/api/v1/health/ready"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, waiting.StatusCode);
        Assert.Contains("tasks.TaskItems", await TablesAsync(database));
        Assert.Contains("public.IdempotencyKeys", await TablesAsync(database));

        _ = api.Monolith.Services;
        await AssertReadyAsync(tasks);
        await AssertReadyAsync(monolith);
        Assert.Equal(TablesInTheirFeatureSchema, await TablesAsync(database));
        await AssertBaselinedAndMovedAsync(database);
    }

    [Fact]
    public async Task EmptyDatabase_IsCreatedByTheContextsOfEachFeature_AndIsReady()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);

        using HttpClient client = StartedClient(database);

        await AssertReadyAsync(client);
        Assert.Empty(await HistoryAsync(database, "public"));
        await AssertBaselinedAndMovedAsync(database);
    }

    [Fact]
    public async Task InheritedAndEmptyDatabases_EndWithTheSameSchema_AndEveryTableInItsFeatureSchema()
    {
        string inherited = await InheritedDatabaseAsync();
        string empty = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);

        using (HttpClient first = StartedClient(inherited))
        using (HttpClient second = StartedClient(empty))
        {
            await AssertReadyAsync(first);
            await AssertReadyAsync(second);
        }

        Assert.Equal(TablesInTheirFeatureSchema, await TablesAsync(empty));
        Assert.Equal(TablesInTheirFeatureSchema, await TablesAsync(inherited));
        Assert.Equal(await SchemaAsync(empty), await SchemaAsync(inherited));
    }

    [Fact]
    public async Task RollingBackEveryMoveMigration_PutsEveryTableBackInPublic()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);
        ApiPair api = _fixture.CreateIsolatedApi(new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database }, _ => { });
        using HttpClient client = api.CreateClient();
        await AssertReadyAsync(client);

        foreach (IServiceProvider host in new[] { api.Monolith.Services, api.Tasks.Services })
        {
            using IServiceScope scope = host.CreateScope();
            foreach (DbContext context in scope.ServiceProvider.ContextsThisHostMigrates())
                await context.GetService<IMigrator>().MigrateAsync(context.Database.GetMigrations().First());
        }

        Assert.Equal(
            TablesInTheirFeatureSchema
                .Select(table => TablesBornInTheirFeatureSchema.Contains(table) ? table : $"public.{table[(table.IndexOf('.', StringComparison.Ordinal) + 1)..]}")
                .Order(StringComparer.Ordinal),
            await TablesAsync(database));
    }

    private async Task<string> InheritedDatabaseAsync()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);
        using HttpClient client = _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database },
            TestDatabases.SkipInitialization,
            out IServiceProvider services);
        using IServiceScope scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>().MigrateAsync(MainRelease);
        return database;
    }

    private HttpClient StartedClient(string database) => StartedClient(database, out _);

    private HttpClient StartedClient(string database, out IServiceProvider services) =>
        _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database },
            out services);

    private static async Task AssertAProjectCanBeCreatedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        db.Projects.Add(Project.Create(Owner, "Transition", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Projects.CountAsync(p => p.OwnerId == Owner));
    }

    private static async Task AssertReadyAsync(HttpClient client)
    {
        using HttpResponseMessage ready = await client.GetAsync("/api/v1/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    private static async Task AssertBaselinedAndMovedAsync(string database)
    {
        foreach ((string schema, string[] migrations) in ContextMigrations)
        {
            string[] history = await HistoryAsync(database, schema);
            Assert.Equal(migrations.Length, history.Length);
            for (int i = 0; i < migrations.Length; i++)
                Assert.EndsWith(migrations[i], history[i], StringComparison.Ordinal);
        }
    }

    private static async Task<string[]> HistoryAsync(string database, string schema)
    {
        string table = $"{schema}.\"__EFMigrationsHistory\"";
        if (await ScalarAsync(database, $"SELECT to_regclass('{table}') IS NULL") is true)
            return [];

        return await LinesAsync(database, $"SELECT \"MigrationId\" FROM {table} ORDER BY 1");
    }

    private static async Task<Dictionary<string, string[]>> RowsAsync(string database)
    {
        Dictionary<string, string[]> rows = [];
        foreach (string table in DataTables)
            rows[table] = await LinesAsync(database, $"SELECT (to_jsonb(t) - '{{{string.Join(',', ColumnsAddedAfterTheMove)}}}'::text[])::text FROM \"{table}\" t ORDER BY 1");
        return rows;
    }

    private static async Task<string[]> SchemaAsync(string database) =>
    [
        .. await LinesAsync(database, """
            SELECT concat_ws(' | ', 'column', table_schema, table_name, column_name, data_type, udt_name, character_maximum_length,
                             is_nullable, column_default, is_identity, identity_generation)
            FROM information_schema.columns
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND strpos(table_name, '__EFMigrationsHistory') = 0
            ORDER BY 1
            """),
        .. await LinesAsync(database, """
            SELECT concat_ws(' | ', 'constraint', n.nspname || '.' || r.relname, c.conname, c.contype, pg_get_constraintdef(c.oid))
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            JOIN pg_class r ON r.oid = c.conrelid
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND r.relname <> '__EFMigrationsHistory'
            ORDER BY 1
            """),
        .. await LinesAsync(database, """
            SELECT concat_ws(' | ', 'index', schemaname, tablename, indexname, indexdef)
            FROM pg_indexes
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema') AND strpos(tablename, '__EFMigrationsHistory') = 0
            ORDER BY 1
            """),
    ];

    private static Task<string[]> TablesAsync(string database) =>
        LinesAsync(database, """
            SELECT n.nspname || '.' || c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND c.relname <> '__EFMigrationsHistory'
            ORDER BY n.nspname || '.' || c.relname COLLATE "C"
            """);

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("owner", Owner);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<string[]> LinesAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand command = new(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<string> lines = [];
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return [.. lines];
    }
}
