using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
using Xunit;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

public sealed class SnapshotTaskCountsTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private IsolatedApi? _api;

    public SnapshotTaskCountsTests(PostgresFixture postgres, RabbitMqFixture broker)
    {
        _broker = broker.Uri;
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task TheSnapshot_CountsEveryProject_AndListsEveryEventStillInTheOutbox()
    {
        _api = await IsolatedApi.StartAsync(_server, _broker);
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

        await _api.DrainAsync();

        using JsonDocument delivered = await SnapshotAsync();
        Assert.Equal(4, delivered.RootElement.GetProperty("undeliveredMessageIds").GetArrayLength());
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
