using System.Security.Claims;

namespace TemplateApp.Features.Auth.CloseSession;

public sealed class CloseSession
{
    private readonly Sessions _sessions;

    public CloseSession(Sessions sessions)
    {
        _sessions = sessions;
    }

    public async Task ExecuteAsync(ClaimsPrincipal caller, CancellationToken ct)
    {
        if (!SessionPrincipal.TryRead(caller, out _, out Guid sessionId))
            return;

        await _sessions.CloseAsync(sessionId, ct);
    }
}
