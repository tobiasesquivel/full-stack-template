using System.Net;
using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Users;

internal sealed class OwnedTasksClient
{
    private readonly HttpClient _http;

    public OwnedTasksClient(HttpClient http)
    {
        _http = http;
    }

    public async Task DeleteOwnedTasksAsync(string ownerId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, $"internal/v1/owners/{Uri.EscapeDataString(ownerId)}/tasks");
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.NoContent);
    }
}
