using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Domain.Entities;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.Update;

public sealed class UpdateTaskCommandHandler(AppDbContext _db)
{
    public async Task<ErrorOr<TaskItemResponse>> Handle(UpdateTaskCommand command, CancellationToken ct)
    {
        TaskItem? task = await _db.TaskItems
            .SingleOrDefaultAsync(t => t.Id == command.Id && t.AuthId == command.AuthId, ct);

        if (task is null)
            return TaskErrors.NotFound(command.Id);

        task.Title = command.Title;
        task.UpdatedAt = DateTime.UtcNow;

        return new TaskItemResponse(task.Id, task.Title, task.IsCompleted, task.CreatedAt);
    }
}
