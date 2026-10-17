using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks;

public sealed partial class TasksModule
{
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;
    private readonly TimeProvider _clock;

    public TasksModule(
        AppDbContext db,
        IAccessPolicy access,
        TimeProvider clock)
    {
        _db = db;
        _access = access;
        _clock = clock;
    }

    private static TaskResponse ToResponse(TaskItem item) =>
        new(item.Id, item.Title, item.IsCompleted, item.CreatedAt, item.UpdatedAt);
}
