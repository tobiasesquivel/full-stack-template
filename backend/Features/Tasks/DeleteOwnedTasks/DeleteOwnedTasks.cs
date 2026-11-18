using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

internal sealed class DeleteOwnedTasks
{
    private readonly TasksDbContext _db;

    public DeleteOwnedTasks(TasksDbContext db)
    {
        _db = db;
    }

    public async Task ExecuteAsync(string ownerId, CancellationToken ct)
    {
        _db.TaskItems.RemoveRange(await _db.TaskItems.Where(t => t.OwnerId == ownerId).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }
}
