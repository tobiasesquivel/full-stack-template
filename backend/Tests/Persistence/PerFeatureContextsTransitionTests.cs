using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Persistence;

public sealed class PerFeatureContextsTransitionTests : IClassFixture<ApiFixture>
{
    private const string MainRelease = "20261117144432_ContractTaskIsCompleted";
    private const string Owner = "owner-transition";
    private static readonly string[] ContextSchemas = ["auth", "users", "tasks", "platform"];
    private static readonly string[] DataTables =
        ["AspNetUsers", "AspNetUserRoles", "Sessions", "UserProfiles", "TaskItems", "IdempotencyKeys", "AuditEntries"];

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

        using HttpClient client = StartedClient(database);

        await AssertReadyAsync(client);
        Dictionary<string, string[]> after = await RowsAsync(database);
        foreach (string table in DataTables)
            Assert.Equal(before[table], after[table]);
        foreach (string schema in ContextSchemas)
            Assert.EndsWith("_InitialCreate", Assert.Single(await HistoryAsync(database, schema)), StringComparison.Ordinal);
        Assert.Contains(MainRelease, await HistoryAsync(database, "public"));
    }

    [Fact]
    public async Task EmptyDatabase_IsCreatedByTheContextsOfEachFeature_AndIsReady()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);

        using HttpClient client = StartedClient(database);

        await AssertReadyAsync(client);
        Assert.Empty(await HistoryAsync(database, "public"));
        foreach (string schema in ContextSchemas)
            Assert.EndsWith("_InitialCreate", Assert.Single(await HistoryAsync(database, schema)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InheritedAndEmptyDatabases_EndWithTheSameSchema()
    {
        string inherited = await InheritedDatabaseAsync();
        string empty = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);

        using (HttpClient first = StartedClient(inherited))
        using (HttpClient second = StartedClient(empty))
        {
            await AssertReadyAsync(first);
            await AssertReadyAsync(second);
        }

        Assert.Equal(await SchemaAsync(empty), await SchemaAsync(inherited));
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

    private HttpClient StartedClient(string database) =>
        _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database },
            out _);

    private static async Task AssertReadyAsync(HttpClient client)
    {
        using HttpResponseMessage ready = await client.GetAsync("/api/v1/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
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
            rows[table] = await LinesAsync(database, $"SELECT row_to_json(t)::text FROM \"{table}\" t ORDER BY 1");
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
            SELECT concat_ws(' | ', 'constraint', c.conrelid::regclass::text, c.conname, c.contype, pg_get_constraintdef(c.oid))
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND strpos(c.conrelid::regclass::text, '__EFMigrationsHistory') = 0
            ORDER BY 1
            """),
        .. await LinesAsync(database, """
            SELECT concat_ws(' | ', 'index', schemaname, tablename, indexname, indexdef)
            FROM pg_indexes
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema') AND strpos(tablename, '__EFMigrationsHistory') = 0
            ORDER BY 1
            """),
    ];

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = new(database);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("owner", Owner);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = new(database);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<string[]> LinesAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = new(database);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<string> lines = [];
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return [.. lines];
    }
}
