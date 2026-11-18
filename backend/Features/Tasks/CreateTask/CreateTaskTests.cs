using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.CreateTask;

public sealed class CreateTaskTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CreateTaskTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_Returns201_WithTrimmedTitle()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var response = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "  Primera tarea  " });

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            var created = response.Payload.RootElement;
            Assert.Equal("Primera tarea", created.GetProperty("title").GetString());
            Assert.False(created.GetProperty("isCompleted").GetBoolean());
            Assert.NotEqual(Guid.Empty.ToString(), created.GetProperty("id").GetString());
            Assert.Equal($"/api/v1/tasks/{created.GetProperty("id").GetString()}", response.Location);
            Assert.False(created.TryGetProperty("ownerId", out _), "OwnerId must not appear in task JSON.");
        }
    }

    [Fact]
    public async Task Create_RejectsBlankAndOversizedTitles()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var blank = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "   " });
        blank.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);

        var oversized = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = new string('x', 201) });
        oversized.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, oversized.Status);
    }

    [Fact]
    public async Task Create_SameKeyTwice_ReplaysFirstResponse_AndCreatesOneTask()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var key = KeyHeader(Guid.NewGuid().ToString());

        var first = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        var second = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        first.Payload?.Dispose();
        second.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.Created, second.Status);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Location, second.Location);
        Assert.False(first.Headers.ContainsKey(ReplayedHeader));
        Assert.Equal("true", Assert.Contains(ReplayedHeader, second.Headers));
        Assert.StartsWith("application/json", Assert.Contains("Content-Type", second.Headers), StringComparison.Ordinal);
        Assert.Equal(["Comprar pan"], await TitlesAsync(alice));
    }

    [Fact]
    public async Task Create_QuotedKey_IsTheSameKey()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string key = Guid.NewGuid().ToString();

        var bare = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: KeyHeader(key));
        var quoted = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: KeyHeader($"\"{key}\""));
        bare.Payload?.Dispose();
        quoted.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.Created, quoted.Status);
        Assert.Equal(bare.Text, quoted.Text);
        Assert.Equal("true", Assert.Contains(ReplayedHeader, quoted.Headers));
    }

    [Fact]
    public async Task Create_SameKeyWithOtherTitle_Returns422_AndKeepsFirstTask()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var key = KeyHeader(Guid.NewGuid().ToString());

        var first = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        first.Payload?.Dispose();
        var reused = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar leche" }, headers: key);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        ProblemAssert.Problem(reused, HttpStatusCode.UnprocessableEntity, "Idempotency.KeyReused");
        Assert.Equal(["Comprar pan"], await TitlesAsync(alice));
    }

    [Fact]
    public async Task Create_ConcurrentSameKey_CreatesOneTask_AndBothGetIt()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());
        var key = KeyHeader(Guid.NewGuid().ToString());

        SessionResponse[] responses = await Task.WhenAll(
            tabA.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key),
            tabB.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key));
        foreach (SessionResponse response in responses)
            response.Payload?.Dispose();

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.Status));
        Assert.Equal(responses[0].Text, responses[1].Text);
        Assert.Single(responses, r => r.Headers.ContainsKey(ReplayedHeader));
        Assert.Equal(["Comprar pan"], await TitlesAsync(tabA));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("8e03978e40d54c1c8a9b3f0e2c6d7a1b")]
    [InlineData("{8e03978e-40d5-4c1c-8a9b-3f0e2c6d7a1b}")]
    public async Task Create_MalformedKey_Returns400_AndCreatesNothing(string malformed)
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var response = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: KeyHeader(malformed));

        ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Idempotency.KeyInvalid");
        Assert.Empty(await TitlesAsync(alice));
    }

    [Fact]
    public async Task Create_FailedValidation_DoesNotStoreTheKey()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var key = KeyHeader(Guid.NewGuid().ToString());

        var blank = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "   " }, headers: key);
        var retried = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        blank.Payload?.Dispose();
        retried.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);
        Assert.Equal(HttpStatusCode.Created, retried.Status);
        Assert.False(retried.Headers.ContainsKey(ReplayedHeader));
        Assert.Equal(["Comprar pan"], await TitlesAsync(alice));
    }

    [Fact]
    public async Task Create_KeyIsScopedToTheUser()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        var key = KeyHeader(Guid.NewGuid().ToString());

        var fromAlice = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        var fromBob = await bob.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        fromAlice.Payload?.Dispose();
        fromBob.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.Created, fromBob.Status);
        Assert.NotEqual(fromAlice.Location, fromBob.Location);
        Assert.False(fromBob.Headers.ContainsKey(ReplayedHeader));
        Assert.Equal(["Comprar pan"], await TitlesAsync(alice));
        Assert.Equal(["Comprar pan"], await TitlesAsync(bob));
    }

    [Fact]
    public async Task Create_WithoutKey_CreatesATaskEachTime()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        await alice.CreateTaskAsync("Comprar pan");
        await alice.CreateTaskAsync("Comprar pan");

        Assert.Equal(["Comprar pan", "Comprar pan"], await TitlesAsync(alice));
    }

    [Fact]
    public async Task Create_CountsTheSavedTask_ButNotItsReplayNorARejectedOne()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var key = KeyHeader(Guid.NewGuid().ToString());
        using var created = new RecordedMeasurements(_fixture.TasksServices, "TemplateApp.Tasks", "templateapp.tasks.created");

        var first = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        var replay = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: key);
        var blank = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "   " });
        first.Payload?.Dispose();
        replay.Payload?.Dispose();
        blank.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.Created, replay.Status);
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);
        var measurement = Assert.Single(created.Values);
        Assert.Equal(1, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    [Fact]
    public async Task Create_WithKey_WhenItFailsAfterTheTaskWasSaved_LeavesNeitherKeyNorTask()
    {
        HttpClient client = _fixture.CreateIsolatedTasksClient(
            services => services.ConfigureDbContext<TasksDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<TaskItem>())),
            out IServiceProvider services);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var failed = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Comprar pan" }, headers: KeyHeader(Guid.NewGuid().ToString()));
        failed.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        OwnedRows rows = await services.OwnedRowsAsync(alice.AuthId);
        Assert.Equal((0L, 0L), (rows.Tasks, rows.IdempotencyKeys));
    }

    [Fact]
    public async Task Create_InAnOwnActiveProject_Returns201_WithItsProjectId()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string projectId = await alice.CreateProjectAsync("Mudanza");

        string id = await alice.CreateTaskAsync("Embalar", projectId);

        var fetched = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.Equal(projectId, fetched.Payload.RootElement.GetProperty("projectId").GetString());
    }

    [Fact]
    public async Task Create_WithoutProject_ReturnsANullProjectId()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var created = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Suelta" });

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        using (created.Payload)
            Assert.Equal(JsonValueKind.Null, created.Payload.RootElement.GetProperty("projectId").ValueKind);
    }

    [Fact]
    public async Task Create_InAForeignOrMissingProject_Returns400ProjectId_AndStoresNoTask()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        string foreign = await bob.CreateProjectAsync("Ajeno");

        foreach (string projectId in new[] { foreign, Guid.NewGuid().ToString() })
        {
            var rejected = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Embalar", projectId });
            JsonElement problem = ProblemAssert.Problem(rejected, HttpStatusCode.BadRequest, "Validation.Failed");
            Assert.True(problem.GetProperty("errors").TryGetProperty("ProjectId", out _));
        }

        Assert.Equal(0, (await _fixture.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
    }

    [Fact]
    public async Task Create_InAnArchivedProject_Returns409ProjectArchived_AndStoresNoTask()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string projectId = await alice.CreateProjectAsync("Mudanza");
        uint version = (await alice.ProjectAsync(projectId)).GetProperty("version").GetUInt32();
        var archived = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{projectId}", new { name = "Mudanza", isArchived = true, version });
        archived.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, archived.Status);

        var rejected = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Embalar", projectId });

        ProblemAssert.Problem(rejected, HttpStatusCode.Conflict, "Task.ProjectArchived");
        Assert.Equal(0, (await _fixture.Services.OwnedRowsAsync(alice.AuthId)).Tasks);
    }

    private const string ReplayedHeader = "Idempotent-Replayed";

    private static Dictionary<string, string> KeyHeader(string key) => new() { ["Idempotency-Key"] = key };

    private static async Task<string[]> TitlesAsync(TestSession session)
    {
        var page = await session.SendAsync(HttpMethod.Get, "api/v1/tasks?pageSize=100", csrf: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotNull(page.Payload);
        using (page.Payload)
            return page.Payload.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("title").GetString()!)
                .ToArray();
    }
}
