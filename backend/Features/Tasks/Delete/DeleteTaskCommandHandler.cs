using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Domain.Entities;
using TemplateApp.Features.Tasks.Common;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.Delete;

public sealed class DeleteTaskCommandHandler(AppDbContext _db)
{
    public async Task<ErrorOr<Success>> Handle(DeleteTaskCommand command, CancellationToken ct)
    {
        TaskItem? task = await _db.TaskItems
            .SingleOrDefaultAsync(t => t.Id == command.Id && t.AuthId == command.AuthId, ct);

        if (task is null)
            return TaskErrors.NotFound(command.Id);

        _db.TaskItems.Remove(task);

        return Result.Success;
    }
}
