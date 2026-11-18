using System.Net;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Projects;

internal sealed class TaskCountsClient
{
    private readonly HttpClient _http;

    public TaskCountsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyDictionary<Guid, TaskCounts>> CountByProjectAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0)
            return new Dictionary<Guid, TaskCounts>();

        string query = string.Join('&', projectIds.Distinct().Select(id => $"projectId={id:D}"));
        using HttpRequestMessage request = new(HttpMethod.Get, $"internal/v1/task-counts?{query}");
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.OK);
        TaskCountsReply reply = await response.Content.ReadFromJsonAsync<TaskCountsReply>(ct)
            ?? throw new InvalidOperationException("Tasks answered the task counts without a body.");

        return reply.Counts.ToDictionary(item => item.ProjectId, item => new TaskCounts(item.Total, item.Completed));
    }
}
