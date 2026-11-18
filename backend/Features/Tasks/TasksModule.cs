using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks;

internal sealed class TasksModule : ITasksModule
{
    private readonly AppDbContext _db;

    public TasksModule(AppDbContext db)
    {
        _db = db;
    }

    public async Task DeleteOwnedTasksAsync(string ownerId, CancellationToken ct)
    {
        _db.TaskItems.RemoveRange(await _db.TaskItems.Where(t => t.OwnerId == ownerId).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }
}
