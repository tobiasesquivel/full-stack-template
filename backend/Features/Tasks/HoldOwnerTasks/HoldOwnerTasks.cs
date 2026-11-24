using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Features.Tasks.Telemetry;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Inbox;

namespace TemplateApp.Features.Tasks.HoldOwnerTasks;

internal sealed partial class HoldOwnerTasks
{
    private readonly TasksDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<HoldOwnerTasks> _logger;

    public HoldOwnerTasks(TasksDbContext db, TimeProvider clock, ILogger<HoldOwnerTasks> logger)
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
        bool held = await _db.TryAcceptAsync(context, ct);
        if (held)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            OwnerHold? hold = await _db.OwnerHolds.SingleOrDefaultAsync(h => h.OwnerId == ownerId, ct);
            if (hold is null)
                _db.OwnerHolds.Add(OwnerHold.Hold(ownerId, closureId, now));
            else
                hold.Renew(closureId, now);

            await _db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        if (held)
            LogHeld(closureId, ownerId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: Tasks holds the tasks of user {UserId}")]
    private partial void LogHeld(Guid closureId, string userId);
}
