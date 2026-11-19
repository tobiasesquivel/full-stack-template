using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Projects.RebuildTaskCounters;

internal sealed partial class RebuildTaskCounters
{
    private readonly ProjectsDbContext _db;
    private readonly TaskCountsSnapshotClient _tasks;
    private readonly TimeProvider _clock;
    private readonly ILogger<RebuildTaskCounters> _logger;

    public RebuildTaskCounters(ProjectsDbContext db, TaskCountsSnapshotClient tasks, TimeProvider clock, ILogger<RebuildTaskCounters> logger)
    {
        _db = db;
        _tasks = tasks;
        _clock = clock;
        _logger = logger;
    }

    public Task<bool> IsNeededAsync(CancellationToken ct) =>
        _db.Projects.AnyAsync(p => !_db.TaskCounters.Any(c => c.ProjectId == p.Id), ct);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        await TaskCountersProjection.EnterExclusiveAsync(_db, ct);
        TaskCountsSnapshotReply snapshot = await _tasks.TakeAsync(ct);
        DateTimeOffset now = _clock.GetUtcNow();
        Guid[] projectIds = [.. snapshot.Counts.Select(c => c.ProjectId)];
        int[] totals = [.. snapshot.Counts.Select(c => c.Total)];
        int[] completed = [.. snapshot.Counts.Select(c => c.Completed)];
        Guid[] undelivered = [.. snapshot.UndeliveredMessageIds];

        await _db.TaskCounters.ExecuteDeleteAsync(ct);
        int projects = await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO projects."ProjectTaskCounters" ("ProjectId", "TaskCount", "CompletedTaskCount", "UpdatedAt")
            SELECT p."Id", COALESCE(s.total, 0), COALESCE(s.completed, 0), {now}
            FROM projects."Projects" AS p
            LEFT JOIN unnest({projectIds}, {totals}, {completed}) AS s(project_id, total, completed) ON s.project_id = p."Id"
            ON CONFLICT ("ProjectId") DO UPDATE SET
                "TaskCount" = EXCLUDED."TaskCount",
                "CompletedTaskCount" = EXCLUDED."CompletedTaskCount",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            ct);

        await _db.Set<InboxMessage>().Where(m => m.Consumer == TaskCountersProjection.Consumer).ExecuteDeleteAsync(ct);
        await _db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO projects."InboxMessages" ("MessageId", "Consumer", "ProcessedAt")
            SELECT id, {TaskCountersProjection.Consumer}, {now} FROM unnest({undelivered}) AS id
            ON CONFLICT ("MessageId", "Consumer") DO NOTHING
            """,
            ct);

        await transaction.CommitAsync(ct);
        LogRebuilt(projects, undelivered.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuilt the task counters of {Projects} projects; {Undelivered} undelivered task events were already counted")]
    private partial void LogRebuilt(int projects, int undelivered);
}
