using System.Security.Claims;

using TemplateApp.Infrastructure.Http;

namespace TemplateApp.Features.Auth.CloseSession;

internal sealed class CloseSession
{
    private readonly Sessions _sessions;

    public CloseSession(Sessions sessions)
    {
        _sessions = sessions;
    }

    public async Task ExecuteAsync(ClaimsPrincipal caller, CancellationToken ct)
    {
        if (!SessionCookie.TryRead(caller, out _, out Guid sessionId))
            return;

        await _sessions.CloseAsync(sessionId, ct);
    }
}
