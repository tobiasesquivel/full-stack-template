using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

internal sealed partial class ReleaseOwnerTasks
{
    private readonly TasksDbContext _db;
    private readonly ILogger<ReleaseOwnerTasks> _logger;

    public ReleaseOwnerTasks(TasksDbContext db, ILogger<ReleaseOwnerTasks> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid closureId, string ownerId, EventContext context, CancellationToken ct)
    {
        AccountClosureTrace.Mark(closureId);
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        await OwnerTombstones.LockExclusiveAsync(_db, ownerId, ct);
        int released = 0;
        if (await _db.TryAcceptAsync(context, ct))
        {
            released = await _db.OwnerHolds
                .Where(h => h.OwnerId == ownerId && h.ClosureId == closureId)
                .ExecuteDeleteAsync(ct);
        }

        await transaction.CommitAsync(ct);
        if (released > 0)
            LogReleased(closureId, ownerId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId} was rejected: Tasks releases the tasks of user {UserId}")]
    private partial void LogReleased(Guid closureId, string userId);
}
