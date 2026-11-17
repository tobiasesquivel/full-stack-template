using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.UpdateTask;

internal sealed class UpdateTask
{
    private static readonly UpdateTaskRequestValidator Validator = new();

    private readonly AppDbContext _db;
    private readonly IAuthModule _auth;
    private readonly TimeProvider _clock;
    private readonly TaskMetrics _metrics;

    public UpdateTask(AppDbContext db, IAuthModule auth, TimeProvider clock, TaskMetrics metrics)
    {
        _db = db;
        _auth = auth;
        _clock = clock;
        _metrics = metrics;
    }

    public async Task<ErrorOr<TaskResponse>> ExecuteAsync(string callerId, Guid id, UpdateTaskRequest request, CancellationToken ct)
    {
        var validation = await Validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationErrors.From(validation);

        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _auth.CanAccessAsync(callerId, item, TaskPermissions.Update, ct))
            return TaskErrors.Forbidden;

        string title = TaskItem.NormalizeTitle(request.Title);
        if (item.Version != request.Version)
            return item.Title == title && (item.CompletedAt is not null) == request.IsCompleted
                ? TaskResponses.From(item)
                : TaskErrors.Conflict;

        bool completes = item.CompletedAt is null && request.IsCompleted;
        DateTimeOffset now = _clock.GetUtcNow();
        item.Title = title;
        item.CompletedAt = request.IsCompleted ? item.CompletedAt ?? now : null;
        item.UpdatedAt = now;
        _db.Entry(item).Property(t => t.Version).OriginalValue = request.Version.Value;

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
