using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Projects.UpdateProject;

public sealed class UpdateProjectTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UpdateProjectTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Rename_ThenArchive_ThenRestore_ChangeOnlyThroughEachTransition()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("Mudanza");

        JsonElement renamed = await PutOkAsync(alice, id, "  Mudanza 2026  ", isArchived: false);
        Assert.Equal("Mudanza 2026", renamed.GetProperty("name").GetString());
        Assert.False(renamed.GetProperty("isArchived").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, renamed.GetProperty("updatedAt").ValueKind);

        JsonElement archived = await PutOkAsync(alice, id, "Mudanza 2026", isArchived: true);
        Assert.True(archived.GetProperty("isArchived").GetBoolean());

        JsonElement restored = await PutOkAsync(alice, id, "Mudanza 2026", isArchived: false);
        Assert.False(restored.GetProperty("isArchived").GetBoolean());
        Assert.Equal(["Added", "Modified", "Modified", "Modified"], await OperationsAsync(id));
    }

    [Fact]
    public async Task ArchiveTwice_SecondWritesNothing_AndLeavesNoAuditRow()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("Mudanza");
        JsonElement archived = await PutOkAsync(alice, id, "Mudanza", isArchived: true);
        int auditRows = (await OperationsAsync(id)).Length;

        JsonElement again = await PutOkAsync(alice, id, "Mudanza", isArchived: true);

        Assert.Equal(Version(archived), Version(again));
        Assert.Equal((await alice.ProjectAsync(id)).GetRawText(), again.GetRawText());
        Assert.Equal(auditRows, (await OperationsAsync(id)).Length);
    }

    [Fact]
    public async Task RenameArchived_Returns409Archived_AndChangesNothing()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("Mudanza");
        await PutOkAsync(alice, id, "Mudanza", isArchived: true);
        JsonElement archived = await alice.ProjectAsync(id);
        int auditRows = (await OperationsAsync(id)).Length;

        var rejected = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Otra", isArchived = true, version = Version(archived) });

        ProblemAssert.Problem(rejected, HttpStatusCode.Conflict, "Project.Archived");
        Assert.Equal(archived.GetRawText(), (await alice.ProjectAsync(id)).GetRawText());
        Assert.Equal(auditRows, (await OperationsAsync(id)).Length);
    }

    [Fact]
    public async Task OnePut_CanRenameAndArchive_OrRestoreAndRename()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("Mudanza");

        JsonElement archived = await PutOkAsync(alice, id, "Mudanza vieja", isArchived: true);
        Assert.Equal("Mudanza vieja", archived.GetProperty("name").GetString());
        Assert.True(archived.GetProperty("isArchived").GetBoolean());

        JsonElement restored = await PutOkAsync(alice, id, "Mudanza nueva", isArchived: false);
        Assert.Equal("Mudanza nueva", restored.GetProperty("name").GetString());
        Assert.False(restored.GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task StaleVersion_Returns409Conflict_UnlessTheRequestedStateIsAlreadyStored()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("Mudanza");
        uint stale = Version(await alice.ProjectAsync(id));
        await PutOkAsync(alice, id, "Mudanza 2026", isArchived: false);
        JsonElement current = await alice.ProjectAsync(id);
        int auditRows = (await OperationsAsync(id)).Length;

        var conflict = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Otra", isArchived = false, version = stale });
        ProblemAssert.Problem(conflict, HttpStatusCode.Conflict, "Project.Conflict");

        var repeated = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza 2026", isArchived = false, version = stale });
        Assert.Equal(HttpStatusCode.OK, repeated.Status);
        Assert.NotNull(repeated.Payload);
        using (repeated.Payload)
            Assert.Equal(current.GetRawText(), repeated.Payload.RootElement.GetRawText());
        Assert.Equal(auditRows, (await OperationsAsync(id)).Length);
    }

    [Fact]
    public async Task RenameToAnotherOwnProjectName_Returns409NameTaken()
    {
        var alice = await RegisteredAsync();
        await alice.CreateProjectAsync("Mudanza");
        string id = await alice.CreateProjectAsync("Viaje");
        uint version = Version(await alice.ProjectAsync(id));

        var taken = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "MUDANZA", isArchived = false, version });

        ProblemAssert.Problem(taken, HttpStatusCode.Conflict, "Project.NameTaken");
        Assert.Equal("Viaje", (await alice.ProjectAsync(id)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task RestoreAndRenameToATakenName_Returns409NameTaken_AndLeavesItArchived()
    {
        var alice = await RegisteredAsync();
        await alice.CreateProjectAsync("Mudanza");
        string id = await alice.CreateProjectAsync("Viaje");
        await PutOkAsync(alice, id, "Viaje", isArchived: true);
        JsonElement archived = await alice.ProjectAsync(id);
        int auditRows = (await OperationsAsync(id)).Length;

        var taken = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza", isArchived = false, version = Version(archived) });

        ProblemAssert.Problem(taken, HttpStatusCode.Conflict, "Project.NameTaken");
        Assert.Equal(archived.GetRawText(), (await alice.ProjectAsync(id)).GetRawText());
        Assert.Equal(auditRows, (await OperationsAsync(id)).Length);
    }

    [Fact]
    public async Task Rename_OnlyChangingCase_IsAllowed()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("mudanza");

        JsonElement renamed = await PutOkAsync(alice, id, "Mudanza", isArchived: false);

        Assert.Equal("Mudanza", renamed.GetProperty("name").GetString());
    }

    [Fact]
    public async Task MissingVersion_Returns400_And_MissingProject_Returns404()
    {
        var alice = await RegisteredAsync();
        string id = await alice.CreateProjectAsync("Mudanza");

        var noVersion = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza", isArchived = true });
        JsonElement problem = ProblemAssert.Problem(noVersion, HttpStatusCode.BadRequest, "Validation.Failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("Version", out _));

        var missing = await alice.SendAsync(HttpMethod.Put, $"api/v1/projects/{Guid.NewGuid():D}", new { name = "Mudanza", isArchived = true, version = 1 });
        ProblemAssert.Problem(missing, HttpStatusCode.NotFound, "Project.NotFound");
    }

    private async Task<TestSession> RegisteredAsync()
    {
        var session = new TestSession(_fixture.CreateClient());
        await session.RegisterAsync(TestAccounts.UniqueName("alice"));
        return session;
    }

    private async Task<string[]> OperationsAsync(string id) =>
        [.. (await _fixture.Services.AuditEntriesAsync(id)).Select(row => row.Operation)];

    private static uint Version(JsonElement project) => project.GetProperty("version").GetUInt32();

    private static async Task<JsonElement> PutOkAsync(TestSession session, string id, string name, bool isArchived)
    {
        uint version = Version(await session.ProjectAsync(id));
        var updated = await session.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name, isArchived, version });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.NotNull(updated.Payload);
        using (updated.Payload)
            return updated.Payload.RootElement.Clone();
    }
}
