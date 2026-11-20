using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.CancelAccountClosure;

internal sealed partial class CancelAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly ILogger<CancelAccountClosure> _logger;

    public CancelAccountClosure(UsersDbContext db, ILogger<CancelAccountClosure> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DateTimeOffset?> RestoreAsync(Guid closureId, string userId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        AccountClosureTrace.Mark(closureId);
        UserProfile? profile = await _db.UserProfiles.SingleOrDefaultAsync(p => p.AuthId == userId, ct);
        DateTimeOffset? closingSince = profile?.ClosureChangedAt;
        if (profile is null || !profile.RejectClosure(closureId, reason, now))
            return null;

        LogCompensated(closureId, userId, reason);
        return closingSince ?? now;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId} was rejected ({Reason}): Users puts user {UserId} back to active")]
    private partial void LogCompensated(Guid closureId, string userId, string reason);
}
