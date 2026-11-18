using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.CountTasksByProject;

public sealed class CountTasksByProjectTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CountTasksByProjectTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Counts_EachRequestedProject_AndLeavesOutAProjectWithoutTasks()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string busy = await alice.CreateProjectAsync("Con tareas");
        string empty = await alice.CreateProjectAsync("Sin tareas");
        await alice.CreateTaskAsync("Abierta", busy);
        string done = await alice.CreateTaskAsync("Hecha", busy);
        uint version = await alice.TaskVersionAsync(done);
        SessionResponse completed = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{done}", new { title = "Hecha", isCompleted = true, version });
        completed.Payload?.Dispose();
        await alice.CreateTaskAsync("Suelta");

        using HttpResponseMessage response = await CountAsync($"projectId={busy}&projectId={empty}&projectId={busy}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement only = Assert.Single(body.RootElement.GetProperty("counts").EnumerateArray());
        Assert.Equal(busy, only.GetProperty("projectId").GetString());
        Assert.Equal(2, only.GetProperty("total").GetInt32());
        Assert.Equal(1, only.GetProperty("completed").GetInt32());
    }

    [Fact]
    public async Task NoIdsOrMoreThanAPage_Answers400()
    {
        string tooMany = string.Join('&', Enumerable.Range(0, CountTasksByProject.MaxProjects + 1).Select(_ => $"projectId={Guid.NewGuid()}"));

        using HttpResponseMessage none = await CountAsync(string.Empty);
        using HttpResponseMessage many = await CountAsync(tooMany);

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, many.StatusCode);
    }

    private async Task<HttpResponseMessage> CountAsync(string query)
    {
        using HttpClient tasks = _fixture.CreateTasksClient();
        return await tasks.GetAsync($"internal/v1/task-counts?{query}", TestContext.Current.CancellationToken);
    }
}
