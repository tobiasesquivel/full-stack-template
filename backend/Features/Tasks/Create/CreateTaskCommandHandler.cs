using ErrorOr;
using TemplateApp.Domain.Entities;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.Create;

public sealed class CreateTaskCommandHandler(AppDbContext _db)
{
    public async Task<ErrorOr<TaskItemResponse>> Handle(CreateTaskCommand command, CancellationToken ct)
    {
        TaskItem task = new()
        {
            Id = Guid.NewGuid(),
            Title = command.Title,
            AuthId = command.AuthId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.TaskItems.Add(task);

        return new TaskItemResponse(task.Id, task.Title, task.IsCompleted, task.CreatedAt);
    }
}
