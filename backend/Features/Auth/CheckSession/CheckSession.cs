using ErrorOr;
using TemplateApp.Features.Auth.Access;

namespace TemplateApp.Features.Auth.CheckSession;

internal sealed class CheckSession
{
    private readonly Sessions _sessions;
    private readonly AccessPolicy _access;

    public CheckSession(Sessions sessions, AccessPolicy access)
    {
        _sessions = sessions;
        _access = access;
    }

    public async Task<ErrorOr<CheckSessionResponse>> ExecuteAsync(CheckSessionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.UserId) || !await _sessions.IsActiveAsync(request.SessionId, request.UserId, ct))
            return AuthErrors.SessionInactive;

        AccessGrant grant = await _access.GetGrantAsync(request.UserId, ct);
        return new CheckSessionResponse([.. grant.Permissions.Order(StringComparer.Ordinal)]);
    }
}
