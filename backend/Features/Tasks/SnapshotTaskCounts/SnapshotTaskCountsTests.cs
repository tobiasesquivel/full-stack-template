using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

public sealed class SnapshotTaskCountsTests : IDisposable
{
    private readonly string _server;
    private IsolatedApi? _api;

    public SnapshotTaskCountsTests(PostgresFixture postgres)
    {
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task TheSnapshot_CountsEveryProject_AndListsTheEventsNotYetDelivered()
    {
        _api = await IsolatedApi.StartAsync(_server);
        TestSession alice = await _api.SignUpAsync("alice");
        string busy = await alice.CreateProjectAsync("Con tareas");
        await alice.CreateProjectAsync("Sin tareas");
        await alice.CreateTaskAsync("Abierta", busy);
        string done = await alice.CreateTaskAsync("Hecha", busy);
        await TaskCounterScenario.SetCompletedAsync(alice, done, true);
        await alice.CreateTaskAsync("Suelta");

        using (JsonDocument pending = await SnapshotAsync())
        {
            JsonElement only = Assert.Single(pending.RootElement.GetProperty("counts").EnumerateArray());
            Assert.Equal((busy, 2, 1), (only.GetProperty("projectId").GetString(), only.GetProperty("total").GetInt32(), only.GetProperty("completed").GetInt32()));
            Guid[] undelivered = [.. pending.RootElement.GetProperty("undeliveredMessageIds").EnumerateArray().Select(id => id.GetGuid())];
            Assert.Equal(
                (await _api.Pair.Tasks.Services.AllOutboxRowsAsync()).Select(row => row.MessageId).Order(),
                undelivered.Order());
            Assert.Equal(4, undelivered.Length);
        }

        await _api.DrainOutboxAsync();

        using JsonDocument delivered = await SnapshotAsync();
        Assert.Empty(delivered.RootElement.GetProperty("undeliveredMessageIds").EnumerateArray());
    }

    public void Dispose() => _api?.Dispose();

    private async Task<JsonDocument> SnapshotAsync()
    {
        using HttpClient tasks = _api!.Pair.CreateTasksClient();
        using HttpResponseMessage response = await tasks.GetAsync("internal/v1/task-count-snapshot", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
