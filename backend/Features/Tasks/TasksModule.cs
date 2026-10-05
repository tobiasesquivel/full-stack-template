using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Authorization;

namespace TemplateApp.Features.Tasks;

public sealed class TasksModule
{
    private readonly AppDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly ResourceAuthorizer _authorizer;
    private readonly TimeProvider _clock;
    private readonly CreateTaskRequestValidator _createValidator = new();
    private readonly UpdateTaskRequestValidator _updateValidator = new();

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

    public async Task<ErrorOr<TaskResponse>> CreateAsync(string callerId, CreateTaskRequest request, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.Errors
                .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
                .ToList();

        if (!await _permissions.HasPermissionAsync(callerId, AppPermissions.Tasks.Create, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        var item = new TaskItem
        {
            Id = Guid.NewGuid(),
            OwnerId = callerId,
            Title = request.Title.Trim(),
            IsCompleted = false,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.TaskItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return ToResponse(item);
    }

    public async Task<ErrorOr<TaskListResponse>> ListAsync(string callerId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1)
            return Error.Validation(code: "Page", description: "Page must be greater than or equal to 1.");
        if (pageSize is < 1 or > 100)
            return Error.Validation(code: "PageSize", description: "PageSize must be between 1 and 100.");

        var filtered = _db.TaskItems.Where(t => t.OwnerId == callerId);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TaskResponse(t.Id, t.Title, t.IsCompleted, t.CreatedAt, t.UpdatedAt))
            .AsNoTracking()
            .ToListAsync(ct);

        return new TaskListResponse(items, totalCount, page, pageSize);
    }

    public async Task<ErrorOr<TaskResponse>> GetByIdAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _authorizer.CanAccessAsync(callerId, item, AppPermissions.Tasks.Read, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        return ToResponse(item);
    }

    public async Task<ErrorOr<TaskResponse>> UpdateAsync(string callerId, Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return validation.Errors
                .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
                .ToList();

        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _authorizer.CanAccessAsync(callerId, item, AppPermissions.Tasks.Update, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        item.Title = request.Title.Trim();
        item.IsCompleted = request.IsCompleted;
        item.UpdatedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(ct);

        return ToResponse(item);
    }

    public async Task<ErrorOr<Success>> DeleteAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _authorizer.CanAccessAsync(callerId, item, AppPermissions.Tasks.Delete, ct))
            return Error.Forbidden(code: "Task.Forbidden", description: "Access denied.");

        _db.TaskItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        return Result.Success;
    }

    private static TaskResponse ToResponse(TaskItem item) =>
        new(item.Id, item.Title, item.IsCompleted, item.CreatedAt, item.UpdatedAt);
}
