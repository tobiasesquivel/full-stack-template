using System.Transactions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.WithdrawAccountClosure;

internal sealed partial class WithdrawAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly AccountClosureHistory _history;
    private readonly IAuthModule _auth;
    private readonly TimeProvider _clock;
    private readonly ILogger<WithdrawAccountClosure> _logger;

    public WithdrawAccountClosure(
        UsersDbContext db,
        AccountClosureHistory history,
        IAuthModule auth,
        TimeProvider clock,
        ILogger<WithdrawAccountClosure> logger)
    {
        _db = db;
        _history = history;
        _auth = auth;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, Guid closureId, CancellationToken ct)
    {
        string? ownerId = await _db.AccountClosureHistory
            .Where(e => e.ClosureId == closureId)
            .OrderBy(e => e.Id)
            .Select(e => e.UserId)
            .FirstOrDefaultAsync(ct)
            ?? await _db.AccountClosureSagas
                .Where(s => s.Id == closureId)
                .Select(s => s.UserId)
                .SingleOrDefaultAsync(ct);
        if (ownerId is null)
            return UserErrors.ClosureNotFound(closureId);

        if (!await _auth.CanAccessAsync(callerId, new ClosureOwner(ownerId), UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        using TransactionScope transaction = CrossContextTransaction.Begin();
        UserProfile? profile = (await _db.UserProfiles
            .FromSql($"""SELECT * FROM users."UserProfiles" WHERE "AuthId" = {ownerId} FOR UPDATE""")
            .ToListAsync(ct)).SingleOrDefault();
        DateTimeOffset now = _clock.GetUtcNow();
        if (profile is null || !profile.WithdrawClosure(closureId, now))
            return UserErrors.ClosureNotCancellable(closureId);

        await _db.AccountClosureGracePeriods.Where(s => s.Id == closureId).ExecuteDeleteAsync(ct);
        await _history.RecordAsync(closureId, ownerId, AccountClosureFacts.Cancelled, callerId, null, now, ct);
        await _db.SaveChangesAsync(ct);
        transaction.Complete();
        AccountClosureTrace.Mark(closureId);
        LogWithdrawn(closureId, ownerId, callerId);
        return Result.Success;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: {CallerId} cancelled the scheduled closure of user {UserId} during the grace period")]
    private partial void LogWithdrawn(Guid closureId, string userId, string callerId);
}
