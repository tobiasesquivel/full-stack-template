using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks.DeleteTask;

internal sealed class DeleteTask
{
    private readonly TasksDbContext _db;
    private readonly IAuthModule _auth;

    public DeleteTask(TasksDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _auth.CanAccessAsync(callerId, item, TaskPermissions.Delete, ct))
            return TaskErrors.Forbidden;

        _db.TaskItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        return Result.Success;
    }
}
