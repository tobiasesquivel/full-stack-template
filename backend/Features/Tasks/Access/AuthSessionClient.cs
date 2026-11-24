using System.Net;
using TemplateApp.Infrastructure.Http.Remote;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class AuthSessionClient
{
    private const string SessionInactiveCode = "Auth.SessionInactive";

    private readonly HttpClient _http;

    public AuthSessionClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<string>?> CheckAsync(string userId, Guid sessionId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "internal/v1/session-checks")
        {
            Content = JsonContent.Create(new SessionCheckRequest(userId, sessionId)),
        };
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.OK, HttpStatusCode.NotFound);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await response.EnsureProblemCodeAsync(SessionInactiveCode, ct);
            return null;
        }

        SessionCheckReply? reply = await response.Content.ReadFromJsonAsync<SessionCheckReply>(ct);
        return reply?.Permissions ?? throw new InvalidOperationException("Auth answered a session check without permissions.");
    }
}
