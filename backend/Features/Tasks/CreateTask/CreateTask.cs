using ErrorOr;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed class CreateTask
{
    private static readonly CreateTaskRequestValidator Validator = new();

    private readonly TasksDbContext _db;
    private readonly TaskAccess _access;
    private readonly TimeProvider _clock;
    private readonly TaskMetrics _metrics;
    private readonly ProjectsClient _projects;

    public CreateTask(TasksDbContext db, TaskAccess access, TimeProvider clock, TaskMetrics metrics, ProjectsClient projects)
    {
        _db = db;
        _access = access;
        _projects = projects;
        _clock = clock;
        _metrics = metrics;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, CreateTaskRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        if (!_access.HasPermission(TaskPermissions.Create))
            return TaskErrors.Forbidden;

        if (request.ProjectId is Guid projectId)
        {
            ProjectAssignment? project = await _projects.FindAssignableAsync(callerId, projectId, ct);
            if (project is null)
                return TaskErrors.ProjectNotAssignable;

            if (project.IsArchived)
                return TaskErrors.ProjectArchived;
        }

        var item = TaskItem.Create(callerId, TaskItem.NormalizeTitle(request.Title), request.ProjectId, _clock.GetUtcNow());

        _db.TaskItems.Add(item);
        await _db.SaveChangesAsync(ct);
        _metrics.TaskCreated();

        return TaskResponses.From(item);
    }
}
