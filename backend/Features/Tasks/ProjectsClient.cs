using System.Net;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Tasks;

internal sealed class ProjectsClient
{
    private readonly HttpClient _http;

    public ProjectsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<ProjectAssignment?> FindAssignableAsync(string ownerId, Guid projectId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"internal/v1/owners/{Uri.EscapeDataString(ownerId)}/projects/{projectId:D}");
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.OK, HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        return await response.Content.ReadFromJsonAsync<ProjectAssignment>(ct)
            ?? throw new InvalidOperationException("Projects answered an assignment check without a body.");
    }
}
