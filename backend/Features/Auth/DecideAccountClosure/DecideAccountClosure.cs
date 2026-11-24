using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Features.Auth.Telemetry;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Inbox;

namespace TemplateApp.Features.Auth.DecideAccountClosure;

internal sealed partial class DecideAccountClosure
{
    public const string Consumer = "auth.account-closures";
    public const int LockSpace = 2_610_001;
    public const int SuperAdminsLockKey = 1;

    private readonly AuthDbContext _db;
    private readonly UserManager<AppIdentityUser> _users;
    private readonly TimeProvider _clock;
    private readonly ILogger<DecideAccountClosure> _logger;

    public DecideAccountClosure(AuthDbContext db, UserManager<AppIdentityUser> users, TimeProvider clock, ILogger<DecideAccountClosure> logger)
    {
        _db = db;
        _users = users;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid closureId, string userId, EventContext context, CancellationToken ct)
    {
        AccountClosureTrace.Mark(closureId);
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        if (!await _db.TryAcceptAsync(context, ct) || await _db.AccountClosures.AnyAsync(c => c.ClosureId == closureId, ct))
        {
            await transaction.CommitAsync(ct);
            return;
        }

        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockSpace}, {SuperAdminsLockKey})", ct);
        DateTimeOffset now = _clock.GetUtcNow();
        AppIdentityUser? identity = await _users.FindByIdAsync(userId);
        AccountClosure decision = identity is not null && await IsLastSuperAdminAsync(identity)
            ? AccountClosure.Reject(closureId, userId, AuthErrors.LastSuperAdmin.Code, now)
            : AccountClosure.Complete(closureId, userId, now);
        _db.AccountClosures.Add(decision);
        if (decision.Outcome == AccountClosureOutcome.Completed && identity is not null)
        {
            IdentityResult deleted = await _users.DeleteAsync(identity);
            if (!deleted.Succeeded)
                throw new InvalidOperationException($"Identity did not delete account {userId}: {string.Join("; ", deleted.Errors.Select(e => e.Code))}.");
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        if (decision.Outcome == AccountClosureOutcome.Rejected)
            LogRejected(closureId, userId, decision.Reason!);
        else
            LogDeleted(closureId, userId);
    }

    private async Task<bool> IsLastSuperAdminAsync(AppIdentityUser identity) =>
        await _users.IsInRoleAsync(identity, AppRoles.SuperAdmin)
        && (await _users.GetUsersInRoleAsync(AppRoles.SuperAdmin)).Count == 1;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account closure {ClosureId}: Auth rejects closing user {UserId} ({Reason}); the earlier steps compensate")]
    private partial void LogRejected(Guid closureId, string userId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: Auth deleted the account of user {UserId}, the point of no return")]
    private partial void LogDeleted(Guid closureId, string userId);
}
