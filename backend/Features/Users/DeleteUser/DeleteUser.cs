using System.Globalization;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Features.Users.ScheduleAccountClosure;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Users.DeleteUser;

internal sealed partial class DeleteUser
{
    private readonly UsersDbContext _db;
    private readonly SagaOutbox<UsersDbContext> _sagas;
    private readonly AccountClosureHistory _history;
    private readonly IAuthModule _auth;
    private readonly AccountClosureOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeleteUser> _logger;

    public DeleteUser(
        UsersDbContext db,
        SagaOutbox<UsersDbContext> sagas,
        AccountClosureHistory history,
        IAuthModule auth,
        IOptions<AccountClosureOptions> options,
        TimeProvider clock,
        ILogger<DeleteUser> logger)
    {
        _db = db;
        _sagas = sagas;
        _history = history;
        _auth = auth;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ErrorOr<AccountClosureResponse>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        UserProfile? profile = (await _db.UserProfiles
            .FromSql($"""SELECT * FROM users."UserProfiles" WHERE "AuthId" = {authId} FOR UPDATE""")
            .ToListAsync(ct)).SingleOrDefault();
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _auth.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        if (profile.ClosureStatus is AccountClosureStatus.Scheduled or AccountClosureStatus.Closing)
        {
            await transaction.CommitAsync(ct);
            return profile.ClosureStatus == AccountClosureStatus.Scheduled
                ? new AccountClosureResponse(profile.ClosureId!.Value, "scheduled", profile.ClosesAt)
                : new AccountClosureResponse(profile.ClosureId!.Value, "closing", null);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        Guid closureId = Guid.CreateVersion7(now);
        DateTimeOffset closesAt = DateTimeOffset.FromUnixTimeSeconds((now + _options.GracePeriod).ToUnixTimeSeconds());
        profile.ScheduleClosure(closureId, now, closesAt);
        await _history.RecordAsync(closureId, authId, AccountClosureFacts.Requested, callerId, closesAt.ToString("O", CultureInfo.InvariantCulture), now, ct);
        (AccountClosureGracePeriod saga, object[] messages) = AccountClosureGracePeriod.Open(closureId, authId, now, _options.GracePeriod, _options.Reminders);
        await _sagas.StartAndCommitAsync(saga, messages, ct);
        AccountClosureTrace.Mark(closureId);
        LogScheduled(closureId, authId, closesAt);
        return new AccountClosureResponse(closureId, "scheduled", closesAt);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: Users schedules the closure of user {UserId} at {ClosesAt}, after the grace period")]
    private partial void LogScheduled(Guid closureId, string userId, DateTimeOffset closesAt);
}
