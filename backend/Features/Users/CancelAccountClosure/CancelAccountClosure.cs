using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Users.CancelAccountClosure;

internal sealed partial class CancelAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly UserMetrics _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<CancelAccountClosure> _logger;

    public CancelAccountClosure(UsersDbContext db, UserMetrics metrics, TimeProvider clock, ILogger<CancelAccountClosure> logger)
    {
        _db = db;
        _metrics = metrics;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid closureId, string userId, string reason, EventContext context, CancellationToken ct)
    {
        AccountClosureTrace.Mark(closureId);
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset? requestedAt = null;
        if (await _db.TryAcceptAsync(context, ct))
        {
            UserProfile? profile = await _db.UserProfiles.SingleOrDefaultAsync(p => p.AuthId == userId, ct);
            DateTimeOffset? closingSince = profile?.ClosureChangedAt;
            if (profile is not null && profile.RejectClosure(closureId, reason, now))
            {
                await _db.SaveChangesAsync(ct);
                requestedAt = closingSince ?? now;
            }
        }

        await transaction.CommitAsync(ct);
        if (requestedAt is DateTimeOffset started)
        {
            _metrics.AccountClosureFinished(ClosureOutcome.Rejected, now - started);
            LogCompensated(closureId, userId, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId} was rejected ({Reason}): Users puts user {UserId} back to active")]
    private partial void LogCompensated(Guid closureId, string userId, string reason);
}
