using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.CreateTask;

public sealed class CreateTask
{
    private static readonly CreateTaskRequestValidator Validator = new();

    private readonly AppDbContext _db;
    private readonly IAuthModule _auth;
    private readonly TimeProvider _clock;

    public CreateTask(AppDbContext db, IAuthModule auth, TimeProvider clock)
    {
        _db = db;
        _auth = auth;
        _clock = clock;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, CreateTaskRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        if (!await _auth.HasPermissionAsync(callerId, TaskPermissions.Create, ct))
            return TaskErrors.Forbidden;

        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            OwnerId = callerId,
            Title = TaskItem.NormalizeTitle(request.Title),
            IsCompleted = false,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.TaskItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return TaskResponses.From(item);
    }
}
