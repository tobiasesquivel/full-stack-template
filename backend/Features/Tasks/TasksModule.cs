using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksModule
{
    private readonly AppDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ResourceAuthorizer _authorizer;
    private readonly TimeProvider _clock;

    public TasksModule(
        AppDbContext db,
        IPermissionService permissions,
        ResourceAuthorizer authorizer,
        TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _authorizer = authorizer;
        _clock = clock;
    }

    private static TaskResponse ToResponse(TaskItem item) =>
        new(item.Id, item.Title, item.IsCompleted, item.CreatedAt, item.UpdatedAt);
}
