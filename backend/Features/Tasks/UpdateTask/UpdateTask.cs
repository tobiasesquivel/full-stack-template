using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.UpdateTask;

public sealed class UpdateTask
{
    private static readonly UpdateTaskRequestValidator Validator = new();

    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;
    private readonly TimeProvider _clock;

    public UpdateTask(AppDbContext db, IAccessPolicy access, TimeProvider clock)
    {
        _db = db;
        _access = access;
        _clock = clock;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _access.CanAccessAsync(callerId, item, TaskPermissions.Update, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        item.Title = TaskItem.NormalizeTitle(request.Title);
        item.IsCompleted = request.IsCompleted;
        item.UpdatedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(ct);

        return TaskResponses.From(item);
    }
}
