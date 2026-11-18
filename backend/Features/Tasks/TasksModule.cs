using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks;

internal sealed class TasksModule : ITasksModule
{
    private readonly TasksDbContext _db;

    public TasksModule(TasksDbContext db)
    {
        _db = db;
    }

    public async Task DeleteOwnedTasksAsync(string ownerId, CancellationToken ct)
    {
        _db.TaskItems.RemoveRange(await _db.TaskItems.Where(t => t.OwnerId == ownerId).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, TaskCountsSummary>> CountByProjectAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0)
            return new Dictionary<Guid, TaskCountsSummary>();

        Guid[] ids = [.. projectIds];
        return await _db.TaskItems
            .Where(t => t.ProjectId != null && ids.Contains(t.ProjectId.Value))
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Total = g.Count(), Completed = g.Count(t => t.CompletedAt != null) })
            .ToDictionaryAsync(g => g.ProjectId, g => new TaskCountsSummary(g.Total, g.Completed), ct);
    }
}
