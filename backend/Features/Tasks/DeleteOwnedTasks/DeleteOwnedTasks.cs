using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

internal sealed class DeleteOwnedTasks
{
    private readonly TasksDbContext _db;

    public DeleteOwnedTasks(TasksDbContext db)
    {
        _db = db;
    }

    public async Task ExecuteAsync(string ownerId, DateTimeOffset deletedAt, EventContext context, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        await OwnerTombstones.LockExclusiveAsync(_db, ownerId, ct);
        if (await _db.TryAcceptAsync(context, ct))
        {
            await OwnerTombstones.BuryAsync(_db, ownerId, deletedAt, ct);
            await DeleteAllAsync(ownerId, ct);
        }

        await transaction.CommitAsync(ct);
    }

    public Task ExecuteAsync(string ownerId, CancellationToken ct) => DeleteAllAsync(ownerId, ct);

    private async Task DeleteAllAsync(string ownerId, CancellationToken ct)
    {
        List<TaskItem> owned = await _db.TaskItems.Where(t => t.OwnerId == ownerId).ToListAsync(ct);
        foreach (TaskItem item in owned)
            item.MarkDeleted();

        _db.TaskItems.RemoveRange(owned);
        await _db.SaveChangesAsync(ct);
    }
}
