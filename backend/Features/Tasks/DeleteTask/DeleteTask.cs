using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal sealed class DeleteTask
{
    private readonly TasksDbContext _db;
    private readonly TaskAccess _access;

    public DeleteTask(TasksDbContext db, TaskAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!_access.CanAccess(callerId, item, TaskPermissions.Delete))
            return TaskErrors.Forbidden;

        item.MarkDeleted();
        _db.TaskItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        return Result.Success;
    }
}
