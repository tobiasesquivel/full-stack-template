using System.Globalization;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Access;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Features.Users.ScheduleAccountClosure;
using TemplateApp.Features.Users.Telemetry;
using TemplateApp.Infrastructure.Messaging.Sagas;

namespace TemplateApp.Features.Users.DeleteUser;

internal sealed partial record DeleteUserCommand
{
    public sealed partial class Handler
    {
        private readonly UsersDbContext _db;
        private readonly SagaOutbox<UsersDbContext> _sagas;
        private readonly AccountClosureHistory _history;
        private readonly IAuthModule _auth;
        private readonly AccountClosureOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;

        public Handler(
            UsersDbContext db,
            SagaOutbox<UsersDbContext> sagas,
            AccountClosureHistory history,
            IAuthModule auth,
            IOptions<AccountClosureOptions> options,
            TimeProvider clock,
            ILoggerFactory loggers)
        {
            _db = db;
            _sagas = sagas;
            _history = history;
            _auth = auth;
            _options = options.Value;
            _clock = clock;
            _logger = loggers.CreateLogger(typeof(DeleteUserCommand));
        }

        public async Task<ErrorOr<AccountClosureResponse>> Handle(DeleteUserCommand command, CancellationToken ct)
        {
            string authId = command.AuthId;
            UserProfile? profile = (await _db.UserProfiles
                .FromSql($"""SELECT * FROM users."UserProfiles" WHERE "AuthId" = {authId} FOR UPDATE""")
                .ToListAsync(ct)).SingleOrDefault();
            if (profile is null)
                return UserErrors.NotFound(authId);

            if (!await _auth.CanAccessAsync(command.CallerId, profile, UsersPermissions.Manage, ct))
                return UserErrors.Forbidden;

            if (profile.ClosureStatus is AccountClosureStatus.Scheduled or AccountClosureStatus.Closing)
            {
                return profile.ClosureStatus == AccountClosureStatus.Scheduled
                    ? new AccountClosureResponse(profile.ClosureId!.Value, "scheduled", profile.ClosesAt)
                    : new AccountClosureResponse(profile.ClosureId!.Value, "closing", null);
            }

            DateTimeOffset now = _clock.GetUtcNow();
            Guid closureId = Guid.CreateVersion7(now);
            DateTimeOffset closesAt = DateTimeOffset.FromUnixTimeSeconds((now + _options.GracePeriod).ToUnixTimeSeconds());
            profile.ScheduleClosure(closureId, now, closesAt);
            await _history.RecordAsync(closureId, authId, AccountClosureFacts.Requested, command.CallerId, closesAt.ToString("O", CultureInfo.InvariantCulture), now, ct);
            (AccountClosureGracePeriod saga, object[] messages) = AccountClosureGracePeriod.Open(closureId, authId, now, _options.GracePeriod, _options.Reminders);
            await _sagas.StartAsync(saga, messages);
            AccountClosureTrace.Mark(closureId);
            LogScheduled(closureId, authId, closesAt);
            return new AccountClosureResponse(closureId, "scheduled", closesAt);
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: Users schedules the closure of user {UserId} at {ClosesAt}, after the grace period")]
        private partial void LogScheduled(Guid closureId, string userId, DateTimeOffset closesAt);
    }
}
