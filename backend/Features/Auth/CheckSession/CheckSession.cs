using ErrorOr;
using TemplateApp.Features.Auth.Access;

namespace TemplateApp.Features.Auth.CheckSession;

internal static class CheckSession
{
    public sealed class Handler
    {
        private readonly Sessions _sessions;
        private readonly AccessPolicy _access;

        public Handler(Sessions sessions, AccessPolicy access)
        {
            _sessions = sessions;
            _access = access;
        }

        public async Task<ErrorOr<CheckSessionResponse>> Handle(CheckSessionCommand command, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(command.UserId) || !await _sessions.IsActiveAsync(command.SessionId, command.UserId, ct))
                return AuthErrors.SessionInactive;

            AccessGrant grant = await _access.GetGrantAsync(command.UserId, ct);
            return new CheckSessionResponse([.. grant.Permissions.Order(StringComparer.Ordinal)]);
        }
    }
}
