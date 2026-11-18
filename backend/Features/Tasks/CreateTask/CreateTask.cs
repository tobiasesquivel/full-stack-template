using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed class CreateTask
{
    private static readonly CreateTaskRequestValidator Validator = new();

    private readonly TasksDbContext _db;
    private readonly IAuthModule _auth;
    private readonly TimeProvider _clock;
    private readonly TaskMetrics _metrics;
    private readonly IProjectsModule _projects;

    public CreateTask(TasksDbContext db, IAuthModule auth, TimeProvider clock, TaskMetrics metrics, IProjectsModule projects)
    {
        _db = db;
        _auth = auth;
        _projects = projects;
        _clock = clock;
        _metrics = metrics;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, CreateTaskRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        if (!await _auth.HasPermissionAsync(callerId, TaskPermissions.Create, ct))
            return TaskErrors.Forbidden;

        if (request.ProjectId is Guid projectId)
        {
            ProjectAssignmentInfo? project = await _projects.FindAssignableProjectAsync(callerId, projectId, ct);
            if (project is null)
                return TaskErrors.ProjectNotAssignable;

            if (project.IsArchived)
                return TaskErrors.ProjectArchived;
        }

        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            OwnerId = callerId,
            Title = TaskItem.NormalizeTitle(request.Title),
            ProjectId = request.ProjectId,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.TaskItems.Add(item);
        await _db.SaveChangesAsync(ct);
        _metrics.TaskCreated();

        return TaskResponses.From(item);
    }
}
