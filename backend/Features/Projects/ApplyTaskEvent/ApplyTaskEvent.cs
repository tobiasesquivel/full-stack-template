using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed partial class ApplyTaskEvent
{
    private const int KnownVersion = 1;

    private readonly ProjectsDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApplyTaskEvent> _logger;

    public ApplyTaskEvent(ProjectsDbContext db, TimeProvider clock, ILogger<ApplyTaskEvent> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(Guid messageId, TaskEventRequest request, CancellationToken ct)
    {
        TaskCountersDelta? delta = DeltaFor(request.Type, request.Payload);
        if (delta is null)
            return Result.Success;

        if (request.Version != KnownVersion || request.Payload is null)
            return ProjectErrors.UnreadableTaskEvent(request.Type, request.Version);

        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        if (!await TaskCountersProjection.TryEnterSharedAsync(_db, ct))
            return ProjectErrors.TaskCountersRebuilding;

        DateTimeOffset now = _clock.GetUtcNow();
        if (await _db.TryAcceptAsync(new EventContext(messageId, TaskCountersProjection.Consumer, request.OccurredAt, now), ct)
            && request.Payload.ProjectId is Guid projectId)
        {
            int updated = await _db.TaskCounters
                .Where(c => c.ProjectId == projectId)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(c => c.TaskCount, c => c.TaskCount + delta.Tasks)
                        .SetProperty(c => c.CompletedTaskCount, c => c.CompletedTaskCount + delta.Completed)
                        .SetProperty(c => c.UpdatedAt, now),
                    ct);
            if (updated == 0)
                LogUnknownProject(messageId, request.Type, projectId);
        }

        await transaction.CommitAsync(ct);
        return Result.Success;
    }

    private static TaskCountersDelta? DeltaFor(string type, TaskEventPayload? payload) => type switch
    {
        "tasks.task-created" => new TaskCountersDelta(1, 0),
        "tasks.task-completed" => new TaskCountersDelta(0, 1),
        "tasks.task-reopened" => new TaskCountersDelta(0, -1),
        "tasks.task-deleted" => new TaskCountersDelta(-1, payload?.WasCompleted == true ? -1 : 0),
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Task event {MessageId} ({Type}) names project {ProjectId}, which has no task counters: no effect")]
    private partial void LogUnknownProject(Guid messageId, string type, Guid projectId);
}
