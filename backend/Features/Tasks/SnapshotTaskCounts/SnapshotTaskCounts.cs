using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

internal sealed class SnapshotTaskCounts
{
    private readonly TasksDbContext _db;

    public SnapshotTaskCounts(TasksDbContext db)
    {
        _db = db;
    }

    public async Task<TaskCountsSnapshotResponse> ExecuteAsync(CancellationToken ct)
    {
        await using IDbContextTransaction snapshot = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        List<SnapshotCountsItem> counts = await _db.TaskItems
            .Where(t => t.ProjectId != null)
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new SnapshotCountsItem(g.Key, g.Count(), g.Count(t => t.CompletedAt != null)))
            .ToListAsync(ct);
        List<Guid> undelivered = await _db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .Select(m => m.MessageId)
            .ToListAsync(ct);

        return new TaskCountsSnapshotResponse(counts, undelivered);
    }
}
