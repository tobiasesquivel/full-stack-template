using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.CompleteAccountClosure;

internal sealed partial class CompleteAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly IProjectsModule _projects;
    private readonly IdempotencyKeys _keys;
    private readonly UserMetrics _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<CompleteAccountClosure> _logger;

    public CompleteAccountClosure(
        UsersDbContext db,
        IProjectsModule projects,
        IdempotencyKeys keys,
        UserMetrics metrics,
        TimeProvider clock,
        ILogger<CompleteAccountClosure> logger)
    {
        _db = db;
        _projects = projects;
        _keys = keys;
        _metrics = metrics;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid closureId, string userId, EventContext context, CancellationToken ct)
    {
        AccountClosureTrace.Mark(closureId);
        await _projects.DeleteOwnedProjectsAsync(userId, ct);
        await _keys.ForgetOwnerAsync(userId, ct);

        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset? requestedAt = null;
        if (await _db.TryAcceptAsync(context, ct))
        {
            UserProfile? profile = await _db.UserProfiles.SingleOrDefaultAsync(p => p.AuthId == userId, ct);
            if (profile is not null)
            {
                requestedAt = profile.ClosureChangedAt ?? now;
                profile.MarkDeleted(now);
                _db.UserProfiles.Remove(profile);
                await _db.SaveChangesAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
        if (requestedAt is DateTimeOffset started)
        {
            _metrics.AccountClosureFinished(ClosureOutcome.Completed, now - started);
            LogCompleted(closureId, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId} completed: Users purged the projects, keys and profile of user {UserId}")]
    private partial void LogCompleted(Guid closureId, string userId);
}
