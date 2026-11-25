using System.Data;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging.Outbox;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

internal sealed partial record SnapshotTaskCountsQuery
{
    public sealed class Handler
    {
        private readonly TasksDbContext _db;

        public Handler(TasksDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<TaskCountsSnapshotResponse>> Handle(SnapshotTaskCountsQuery query, CancellationToken ct)
        {
            await using IDbContextTransaction snapshot = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            List<SnapshotCountsItem> counts = await _db.TaskItems
                .Where(t => t.ProjectId != null)
                .GroupBy(t => t.ProjectId!.Value)
                .Select(g => new SnapshotCountsItem(g.Key, g.Count(), g.Count(t => t.CompletedAt != null)))
                .ToListAsync(ct);
            List<Guid> counted = await _db.Set<OutboxMessage>()
                .Select(m => m.MessageId)
                .ToListAsync(ct);

            return new TaskCountsSnapshotResponse(counts, counted);
        }
    }
}
