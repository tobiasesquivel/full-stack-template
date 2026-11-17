using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.ListTasks;

public sealed class ListTasksPlanTests : IClassFixture<ApiFixture>
{
    private const string ListIndex = "IX_TaskItems_OwnerId_CreatedAt_Id";
    private const int PageSize = 10;
    private const int Owners = 200;
    private const int TasksPerOwner = 100;
    private const int CallerTasks = 1_000;
    private const int MaxBlocksPerPage = 2 * (PageSize + 1);

    private readonly ApiFixture _fixture;
    private readonly PostgresFixture _postgres;

    public ListTasksPlanTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _postgres = postgres;
    }

    [Fact]
    public async Task List_ReadsOnlyThePage_ThroughTheListIndex()
    {
        string database = await TestDatabases.CreateEmptyAsync(_postgres.ConnectionString);
        CapturedCommands commands = new();
        HttpClient client = _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["ConnectionStrings:DefaultConnection"] = database },
            services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(commands)),
            out _);
        TestSession alice = new(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await SeedAsync(database, alice.AuthId);

        commands.Reset();
        string? cursor = await ReadNextCursorAsync(alice, $"api/v1/tasks?pageSize={PageSize}");
        Assert.NotNull(cursor);
        await ReadNextCursorAsync(alice, $"api/v1/tasks?pageSize={PageSize}&cursor={cursor}");

        IReadOnlyList<CapturedCommand> pages = commands.Reading("TaskItems");
        Assert.Equal(2, pages.Count);
        for (int page = 0; page < pages.Count; page++)
        {
            QueryPlan plan = await QueryPlan.ExplainAnalyzeAsync(database, pages[page]);
            TestContext.Current.TestOutputHelper?.WriteLine($"Plan: {plan}");

            Assert.True(
                plan.OfType("Index Scan").Any(node => node.IndexName == ListIndex && node.ScanDirection == "Backward"),
                $"The list must walk {ListIndex} backward. Plan: {plan}");
            Assert.False(
                plan.Nodes.Any(node => node.NodeType.EndsWith("Sort", StringComparison.Ordinal)),
                $"The index order must make a sort unnecessary. Plan: {plan}");
            Assert.True(
                plan.Nodes.All(node => node.ActualRows <= PageSize + 1),
                $"No step may return more rows than one page plus the look-ahead row. Plan: {plan}");
            Assert.True(
                plan.Nodes[0].SharedBlocks <= MaxBlocksPerPage,
                $"A page may touch at most one heap block per row plus the index descent. Plan: {plan}");
            Assert.True(
                page == 0 || plan.OfType("Index Scan").Any(node => node.IndexCondition?.Contains("ROW(", StringComparison.Ordinal) == true),
                $"The cursor comparison must narrow the index scan, not filter its rows. Plan: {plan}");
        }
    }

    private static async Task<string?> ReadNextCursorAsync(TestSession session, string route)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, route, csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            JsonElement page = response.Payload.RootElement;
            Assert.Equal(PageSize, page.GetProperty("items").GetArrayLength());
            JsonElement next = page.GetProperty("nextCursor");
            return next.ValueKind == JsonValueKind.Null ? null : next.GetString();
        }
    }

    private static async Task SeedAsync(string database, string callerId)
    {
        await using NpgsqlConnection connection = new(database);
        await connection.OpenAsync();
        await using NpgsqlCommand seed = new(
            """
            INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            SELECT 'owner-' || o, false, false, false, false, 0 FROM generate_series(1, @owners) AS o;

            INSERT INTO "UserProfiles" ("AuthId", "DisplayName")
            SELECT 'owner-' || o, 'Owner ' || o FROM generate_series(1, @owners) AS o;

            INSERT INTO "TaskItems" ("Id", "OwnerId", "Title", "CompletedAt", "CreatedAt")
            SELECT gen_random_uuid(), owner_id, 'Task ' || t, CASE WHEN t % 3 = 0 THEN created_at END, created_at
            FROM (
                SELECT 'owner-' || o AS owner_id, t, now() - make_interval(secs => t) AS created_at
                FROM generate_series(1, @owners) AS o, generate_series(1, @tasksPerOwner) AS t
                UNION ALL
                SELECT @caller, t, now() - interval '1 day' - make_interval(secs => t)
                FROM generate_series(1, @callerTasks) AS t
            ) AS tasks
            ORDER BY random();

            ANALYZE "TaskItems";
            """,
            connection);
        seed.Parameters.AddWithValue("owners", Owners);
        seed.Parameters.AddWithValue("tasksPerOwner", TasksPerOwner);
        seed.Parameters.AddWithValue("caller", callerId);
        seed.Parameters.AddWithValue("callerTasks", CallerTasks);
        await seed.ExecuteNonQueryAsync();
    }
}
