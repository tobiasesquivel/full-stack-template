using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Projects.GetProject;

public sealed class GetProjectTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetProjectTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetById_ReturnsOwn_And_Missing_Returns404()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateProjectAsync("Mudanza");

        Assert.Equal("Mudanza", (await alice.ProjectAsync(id)).GetProperty("name").GetString());

        var missing = await alice.SendAsync(HttpMethod.Get, $"api/v1/projects/{Guid.NewGuid():D}", csrf: false);
        ProblemAssert.Problem(missing, HttpStatusCode.NotFound, "Project.NotFound");
    }

    [Fact]
    public async Task ForeignProject_IsForbidden_Then_AdminReadsAndUpdatesIt()
    {
        var alice = new TestSession(_fixture.CreateClient());
        string aliceName = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(aliceName);
        var bob = new TestSession(_fixture.CreateClient());
        string bobName = TestAccounts.UniqueName("bob");
        await bob.RegisterAsync(bobName);
        string id = await alice.CreateProjectAsync("Mudanza");
        uint version = (await alice.ProjectAsync(id)).GetProperty("version").GetUInt32();

        var read = await bob.SendAsync(HttpMethod.Get, $"api/v1/projects/{id}", csrf: false);
        ProblemAssert.Problem(read, HttpStatusCode.Forbidden, "Project.Forbidden");
        var update = await bob.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza", isArchived = true, version });
        ProblemAssert.Problem(update, HttpStatusCode.Forbidden, "Project.Forbidden");

        await _fixture.SetRoleAsync(bobName, "Admin", member: true);

        var asAdmin = await bob.SendAsync(HttpMethod.Get, $"api/v1/projects/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, asAdmin.Status);
        Assert.NotNull(asAdmin.Payload);
        using (asAdmin.Payload)
            Assert.Equal(aliceName, asAdmin.Payload.RootElement.GetProperty("ownerName").GetString());

        var adminUpdate = await bob.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza", isArchived = true, version });
        adminUpdate.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, adminUpdate.Status);
        Assert.True((await alice.ProjectAsync(id)).GetProperty("isArchived").GetBoolean());
    }
}
