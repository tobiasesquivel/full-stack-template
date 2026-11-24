using ErrorOr;
using TemplateApp.Features.Auth.Access;

namespace TemplateApp.Features.Auth.CloseSession;

internal static class CloseSession
{
    public sealed class Handler
    {
        private readonly Sessions _sessions;

        public Handler(Sessions sessions)
        {
            _sessions = sessions;
        }

        public async Task<ErrorOr<Success>> Handle(CloseSessionCommand command, CancellationToken ct)
        {
            if (command.SessionId is Guid sessionId)
                await _sessions.CloseAsync(sessionId, ct);

            return Result.Success;
        }
    }
}
