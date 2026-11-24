using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Inbox;

namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

internal sealed partial class ReleaseOwnerTasks
{
    private readonly TasksDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReleaseOwnerTasks> _logger;

    public ReleaseOwnerTasks(TasksDbContext db, TimeProvider clock, ILogger<ReleaseOwnerTasks> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid closureId, string ownerId, EventContext context, CancellationToken ct)
    {
        AccountClosureTrace.Mark(closureId);
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        await OwnerTombstones.LockExclusiveAsync(_db, ownerId, ct);
        OwnerHold? hold = null;
        if (await _db.TryAcceptAsync(context, ct))
        {
            hold = await _db.OwnerHolds.SingleOrDefaultAsync(h => h.OwnerId == ownerId && h.ClosureId == closureId, ct);
            if (hold is not null)
            {
                hold.Release(_clock.GetUtcNow());
                _db.OwnerHolds.Remove(hold);
                await _db.SaveChangesAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
        if (hold is not null)
            LogReleased(closureId, ownerId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId} was rejected: Tasks releases the tasks of user {UserId}")]
    private partial void LogReleased(Guid closureId, string userId);
}
