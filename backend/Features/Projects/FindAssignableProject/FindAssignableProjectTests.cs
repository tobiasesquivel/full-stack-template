using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Projects.FindAssignableProject;

public sealed class FindAssignableProjectTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public FindAssignableProjectTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OwnProject_AnswersWhetherItIsArchived()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string active = await alice.CreateProjectAsync("Activo");
        string archived = await alice.CreateProjectAsync("Archivado");
        uint version = (await alice.ProjectAsync(archived)).GetProperty("version").GetUInt32();
        SessionResponse archive = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{archived}", new { name = "Archivado", isArchived = true, version });
        archive.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, archive.Status);

        Assert.False(await IsArchivedAsync(alice.AuthId, active));
        Assert.True(await IsArchivedAsync(alice.AuthId, archived));
    }

    [Fact]
    public async Task ForeignOrUnknownProject_Answers404()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        string bobs = await bob.CreateProjectAsync("De Bob");

        using HttpClient monolith = _fixture.CreateMonolithClient();
        using HttpResponseMessage foreign = await monolith.GetAsync($"internal/v1/owners/{alice.AuthId}/projects/{bobs}", TestContext.Current.CancellationToken);
        using HttpResponseMessage unknown = await monolith.GetAsync($"internal/v1/owners/{alice.AuthId}/projects/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private async Task<bool> IsArchivedAsync(string ownerId, string projectId)
    {
        using HttpClient monolith = _fixture.CreateMonolithClient();
        using HttpResponseMessage response = await monolith.GetAsync($"internal/v1/owners/{ownerId}/projects/{projectId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("isArchived").GetBoolean();
    }
}
