using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.DeleteTask;

public sealed class DeleteTask
{
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;

    public DeleteTask(AppDbContext db, IAccessPolicy access)
    {
        _db = db;
        _access = access;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, Guid id, CancellationToken ct)
    {
        var item = await _db.TaskItems.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
            return TaskErrors.NotFound(id);

        if (!await _access.CanAccessAsync(callerId, item, TaskPermissions.Delete, ct))
            return TaskErrors.Forbidden;

        _db.TaskItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        return Result.Success;
    }
}
