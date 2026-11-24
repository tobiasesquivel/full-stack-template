using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Projects.CreateProject;

public sealed class CreateProjectTests : IClassFixture<ApiFixture>
{
    private const string ReplayedHeader = "Idempotent-Replayed";

    private readonly ApiFixture _fixture;

    public CreateProjectTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_Returns201_WithTrimmedName_OwnerName_AndNoTasks()
    {
        var alice = new TestSession(_fixture.CreateClient());
        string aliceName = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(aliceName);

        var response = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "  Mudanza  " });

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            JsonElement created = response.Payload.RootElement;
            Assert.Equal("Mudanza", created.GetProperty("name").GetString());
            Assert.False(created.GetProperty("isArchived").GetBoolean());
            Assert.Equal(aliceName, created.GetProperty("ownerName").GetString());
            Assert.Equal(0, created.GetProperty("taskCount").GetInt32());
            Assert.Equal(0, created.GetProperty("completedTaskCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, created.GetProperty("updatedAt").ValueKind);
            Assert.Equal($"/api/v1/projects/{created.GetProperty("id").GetString()}", response.Location);
            Assert.False(created.TryGetProperty("ownerId", out _), "OwnerId must not appear in project JSON.");
        }
    }

    [Fact]
    public async Task Create_RejectsBlankAndOversizedNames_AndStoresNothing()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        foreach (string name in new[] { "   ", new string('x', Project.NameMaxLength + 1) })
        {
            var rejected = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name });
            JsonElement problem = ProblemAssert.Problem(rejected, HttpStatusCode.BadRequest, "Validation.Failed");
            Assert.True(problem.GetProperty("errors").TryGetProperty("Name", out _));
        }

        Assert.Equal(0, await ProjectRowsAsync(alice.AuthId));
        await alice.CreateProjectAsync(new string('x', Project.NameMaxLength));
    }

    [Fact]
    public async Task Create_SameNameWithOtherCasing_Returns409NameTaken_AndKeepsOneRow()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        await alice.CreateProjectAsync("Mudanza");

        var duplicate = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = " MUDANZA " });

        ProblemAssert.Problem(duplicate, HttpStatusCode.Conflict, "Project.NameTaken");
        Assert.Equal(1, await ProjectRowsAsync(alice.AuthId));
        await bob.CreateProjectAsync("Mudanza");
    }

    [Fact]
    public async Task Create_ConcurrentSameName_WithDifferentKeys_CreatesOneProject()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());

        SessionResponse[] responses = await Task.WhenAll(
            tabA.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "Mudanza" }, headers: KeyHeader(Guid.NewGuid().ToString())),
            tabB.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "mudanza" }, headers: KeyHeader(Guid.NewGuid().ToString())));

        SessionResponse created = Assert.Single(responses, r => r.Status == HttpStatusCode.Created);
        created.Payload?.Dispose();
        ProblemAssert.Problem(Assert.Single(responses, r => r.Status != HttpStatusCode.Created), HttpStatusCode.Conflict, "Project.NameTaken");
        Assert.Equal(1, await ProjectRowsAsync(tabA.AuthId));
        Assert.Equal(1, await _fixture.IdempotencyKeyCountAsync(tabA.AuthId));
    }

    [Fact]
    public async Task TheUniqueIndex_RejectsASecondProjectWithTheSameNormalizedName()
    {
        string owner = $"owner-{Guid.NewGuid():N}";
        await SaveAsync(Project.Create(owner, "Mudanza", DateTimeOffset.UtcNow));

        DbUpdateException rejected = await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(Project.Create(owner, "MUDANZA", DateTimeOffset.UtcNow)));

        Assert.True(ProjectNameUniqueness.IsViolatedBy(rejected), rejected.InnerException?.Message);
        Assert.Equal(1, await ProjectRowsAsync(owner));
    }

    [Fact]
    public async Task Create_SameKeyTwice_ReplaysFirstResponse_AndCreatesOneProject()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var key = KeyHeader(Guid.NewGuid().ToString());

        var first = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "Mudanza" }, headers: key);
        var second = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "Mudanza" }, headers: key);
        first.Payload?.Dispose();
        second.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.Created, second.Status);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Location, second.Location);
        Assert.Equal("true", Assert.Contains(ReplayedHeader, second.Headers));
        Assert.Equal(1, await ProjectRowsAsync(alice.AuthId));
    }

    private static Dictionary<string, string> KeyHeader(string key) => new() { ["Idempotency-Key"] = key };

    private Task<long> ProjectRowsAsync(string ownerId) =>
        _fixture.Services.CountAsync($"""SELECT count(*) AS "Value" FROM projects."Projects" WHERE "OwnerId" = {ownerId}""");

    private async Task SaveAsync(Project project)
    {
        using IServiceScope scope = _fixture.Services.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
    }
}
