using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal static class DeleteTask
{
    public sealed class Handler
    {
        private readonly TasksDbContext _db;

        public Handler(TasksDbContext db)
        {
            _db = db;
        }

        public async Task<ErrorOr<Success>> Handle(DeleteTaskCommand command, CancellationToken ct)
        {
            var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == command.Id, ct);
            if (item is null)
                return TaskErrors.NotFound(command.Id);

            if (!command.Caller.CanAccess(item, TaskPermissions.Delete))
                return TaskErrors.Forbidden;

            item.MarkDeleted();
            _db.TaskItems.Remove(item);
            await _db.SaveChangesAsync(ct);

            return Result.Success;
        }
    }
}
