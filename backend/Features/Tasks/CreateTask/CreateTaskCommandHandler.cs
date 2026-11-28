using System.Transactions;
using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Features.Tasks.Telemetry;
using TemplateApp.Infrastructure.Persistence.Transactions;

namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed partial record CreateTaskCommand
{
    public sealed class Handler
    {
        private readonly TasksDbContext _db;
        private readonly TimeProvider _clock;
        private readonly TaskMetrics _metrics;
        private readonly ProjectsClient _projects;

        public Handler(TasksDbContext db, TimeProvider clock, TaskMetrics metrics, ProjectsClient projects)
        {
            _db = db;
            _projects = projects;
            _clock = clock;
            _metrics = metrics;
        }

        public async Task<ErrorOr<TaskResponse>> Handle(CreateTaskCommand command, CancellationToken ct)
        {
            if (!command.Caller.HasPermission(TasksPermissions.Create))
                return TaskErrors.Forbidden;

            if (command.ProjectId is Guid projectId)
            {
                ProjectAssignment? project = await _projects.FindAssignableAsync(command.Caller.Id, projectId, ct);
                if (project is null)
                    return TaskErrors.ProjectNotAssignable;

                if (project.IsArchived)
                    return TaskErrors.ProjectArchived;
            }

            using TransactionScope transaction = CrossContextTransaction.Begin();
            ErrorOr<LiveOwner> owner = await OwnerTombstones.FindLiveAsync(_db, command.Caller.Id, ct);
            if (owner.IsError)
                return owner.Errors;

            var item = TaskItem.Create(owner.Value, TaskItem.NormalizeTitle(command.Title), command.ProjectId, _clock.GetUtcNow());

            _db.TaskItems.Add(item);
            await _db.SaveChangesAsync(ct);
            transaction.Complete();
            _metrics.TaskCreated();

            return TaskResponses.From(item);
        }
    }
}
