using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed partial class ApplyTaskEvent
{
    private readonly ProjectsDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApplyTaskEvent> _logger;

    public ApplyTaskEvent(ProjectsDbContext db, TimeProvider clock, ILogger<ApplyTaskEvent> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string type, Guid? projectId, TaskCountersDelta delta, EventContext context, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        if (!await TaskCountersProjection.TryEnterSharedAsync(_db, ct))
            return ProjectErrors.TaskCountersRebuilding;

        if (await _db.TryAcceptAsync(context with { Consumer = TaskCountersProjection.Consumer }, ct)
            && projectId is Guid project)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            int updated = await _db.TaskCounters
                .Where(c => c.ProjectId == project)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(c => c.TaskCount, c => c.TaskCount + delta.Tasks)
                        .SetProperty(c => c.CompletedTaskCount, c => c.CompletedTaskCount + delta.Completed)
                        .SetProperty(c => c.UpdatedAt, now),
                    ct);
            if (updated == 0)
                LogUnknownProject(context.MessageId, type, project);
        }

        await transaction.CommitAsync(ct);
        return Result.Success;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Task event {MessageId} ({Type}) names project {ProjectId}, which has no task counters: no effect")]
    private partial void LogUnknownProject(Guid messageId, string type, Guid projectId);
}
