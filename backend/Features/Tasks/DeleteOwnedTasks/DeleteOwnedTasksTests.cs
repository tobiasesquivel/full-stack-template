using System.Net;
using System.Text.Json;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

public sealed class DeleteOwnedTasksTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public DeleteOwnedTasksTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeletesOnlyTheOwnersTasks_AndRepeatingItChangesNothing()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        await alice.CreateTaskAsync("De Alice");
        await bob.CreateTaskAsync("De Bob");

        using HttpResponseMessage first = await DeleteAsync(alice.AuthId, actor: null);
        using HttpResponseMessage again = await DeleteAsync(alice.AuthId, actor: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(0, await CountAsync(alice));
        Assert.Equal(1, await CountAsync(bob));
    }

    [Fact]
    public async Task TheAuditNamesTheUserTheCallerActsFor()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Auditada");

        using HttpResponseMessage deleted = await DeleteAsync(alice.AuthId, actor: "admin-42");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        AuditRow row = (await _fixture.Services.AuditEntriesAsync(id)).Last();
        Assert.Equal(("Deleted", "user", "admin-42"), (row.Operation, row.ActorKind, row.ActorId));
    }

    private async Task<HttpResponseMessage> DeleteAsync(string ownerId, string? actor)
    {
        using HttpClient tasks = _fixture.CreateTasksClient();
        using HttpRequestMessage request = new(HttpMethod.Delete, $"internal/v1/owners/{ownerId}/tasks");
        if (actor is not null)
            request.Headers.Add(OnBehalfOf.Header, actor);
        return await tasks.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<int> CountAsync(TestSession session)
    {
        SessionResponse list = await session.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.NotNull(list.Payload);
        using (list.Payload)
            return list.Payload.RootElement.GetProperty("items").GetArrayLength();
    }
}
