using System.Net;
using TemplateApp.Infrastructure.Http.Remote;

namespace TemplateApp.Features.Projects.RebuildTaskCounters;

internal sealed class TaskCountsSnapshotClient
{
    private readonly HttpClient _http;

    public TaskCountsSnapshotClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<TaskCountsSnapshotReply> TakeAsync(CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "internal/v1/task-count-snapshot");
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<TaskCountsSnapshotReply>(ct)
            ?? throw new InvalidOperationException("Tasks answered the task count snapshot without a body.");
    }
}
