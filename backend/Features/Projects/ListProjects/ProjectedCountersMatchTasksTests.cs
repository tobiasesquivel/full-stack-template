using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Projects.ListProjects;

public sealed class ProjectedCountersMatchTasksTests : IDisposable
{
    private readonly string _server;
    private IsolatedApi? _api;

    public ProjectedCountersMatchTasksTests(PostgresFixture postgres)
    {
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task TheProjection_AnswersWhatTasksCountsLive()
    {
        _api = await IsolatedApi.StartAsync(_server);
        IReadOnlyList<TestSession> owners = await TaskCounterScenario.SeedAsync(_api);
        await _api.DrainOutboxAsync();

        foreach (TestSession owner in owners)
        {
            Dictionary<string, (int, int)> listed = await ListedCountersAsync(owner);
            Assert.NotEmpty(listed);
            Assert.Equal(await LiveCountersAsync(listed.Keys), listed);
            foreach (string projectId in listed.Keys)
            {
                JsonElement detail = await owner.ProjectAsync(projectId);
                Assert.Equal(listed[projectId], (detail.GetProperty("taskCount").GetInt32(), detail.GetProperty("completedTaskCount").GetInt32()));
            }
        }
    }

    public void Dispose() => _api?.Dispose();

    private static async Task<Dictionary<string, (int, int)>> ListedCountersAsync(TestSession owner)
    {
        SessionResponse page = await owner.SendAsync(HttpMethod.Get, "api/v1/projects?pageSize=100", csrf: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotNull(page.Payload);
        using (page.Payload)
        {
            return page.Payload.RootElement.GetProperty("items").EnumerateArray().ToDictionary(
                item => item.GetProperty("id").GetString()!,
                item => (item.GetProperty("taskCount").GetInt32(), item.GetProperty("completedTaskCount").GetInt32()));
        }
    }

    private async Task<Dictionary<string, (int, int)>> LiveCountersAsync(IEnumerable<string> projectIds)
    {
        string[] ids = [.. projectIds];
        using HttpClient tasks = _api!.Pair.CreateTasksClient();
        string query = string.Join('&', ids.Select(id => $"projectId={id}"));
        using HttpResponseMessage response = await tasks.GetAsync($"internal/v1/task-counts?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Dictionary<string, (int, int)> counted = body.RootElement.GetProperty("counts").EnumerateArray().ToDictionary(
            item => item.GetProperty("projectId").GetString()!,
            item => (item.GetProperty("total").GetInt32(), item.GetProperty("completed").GetInt32()));
        return ids.ToDictionary(id => id, id => counted.GetValueOrDefault(id, (0, 0)));
    }
}
