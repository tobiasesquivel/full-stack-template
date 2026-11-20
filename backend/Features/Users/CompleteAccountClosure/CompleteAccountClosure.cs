using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.CompleteAccountClosure;

internal sealed partial class CompleteAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly IProjectsModule _projects;
    private readonly IdempotencyKeys _keys;
    private readonly ILogger<CompleteAccountClosure> _logger;

    public CompleteAccountClosure(UsersDbContext db, IProjectsModule projects, IdempotencyKeys keys, ILogger<CompleteAccountClosure> logger)
    {
        _db = db;
        _projects = projects;
        _keys = keys;
        _logger = logger;
    }

    public async Task<DateTimeOffset?> PurgeAsync(Guid closureId, string userId, DateTimeOffset now, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The profile purge saves inside the transaction of the closure step that calls it.");

        AccountClosureTrace.Mark(closureId);
        await _projects.DeleteOwnedProjectsAsync(userId, ct);
        await _keys.ForgetOwnerAsync(userId, ct);
        UserProfile? profile = await _db.UserProfiles.SingleOrDefaultAsync(p => p.AuthId == userId, ct);
        if (profile is null)
            return null;

        DateTimeOffset closingSince = profile.ClosureChangedAt ?? now;
        profile.MarkDeleted(now);
        _db.UserProfiles.Remove(profile);
        LogCompleted(closureId, userId);
        return closingSince;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId} completed: Users purged the projects, keys and profile of user {UserId}")]
    private partial void LogCompleted(Guid closureId, string userId);
}
