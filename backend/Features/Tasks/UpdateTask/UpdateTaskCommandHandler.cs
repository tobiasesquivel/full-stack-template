using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Features.Tasks.Telemetry;

namespace TemplateApp.Features.Tasks.UpdateTask;

internal sealed partial record UpdateTaskCommand
{
    public sealed class Handler
    {
        private readonly TasksDbContext _db;
        private readonly TimeProvider _clock;
        private readonly TaskMetrics _metrics;

        public Handler(TasksDbContext db, TimeProvider clock, TaskMetrics metrics)
        {
            _db = db;
            _clock = clock;
            _metrics = metrics;
        }

        public async Task<ErrorOr<TaskResponse>> Handle(UpdateTaskCommand command, CancellationToken ct)
        {
            var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == command.Id, ct);
            if (item is null)
                return TaskErrors.NotFound(command.Id);

            if (!command.Caller.CanAccess(item, TaskPermissions.Update))
                return TaskErrors.Forbidden;

            string title = TaskItem.NormalizeTitle(command.Title);
            if (item.Version != command.Version)
                return item.Title == title && (item.CompletedAt is not null) == command.IsCompleted
                    ? TaskResponses.From(item)
                    : TaskErrors.Conflict;

            bool completes = item.CompletedAt is null && command.IsCompleted;
            DateTimeOffset now = _clock.GetUtcNow();
            item.Update(title, command.IsCompleted, now);
            _db.Entry(item).Property(t => t.Version).OriginalValue = command.Version!.Value;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return TaskErrors.Conflict;
            }

            if (completes)
                _metrics.TaskCompleted(now - item.CreatedAt);

            return TaskResponses.From(item);
        }
    }
}
