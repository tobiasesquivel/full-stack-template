using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Persistence;

namespace TemplateApp.Features.Tasks.CountTasksByProject;

internal sealed class CountTasksByProject
{
    public const int MaxProjects = 100;

    private readonly TasksDbContext _db;

    public CountTasksByProject(TasksDbContext db)
    {
        _db = db;
    }

    public async Task<ErrorOr<TaskCountsResponse>> ExecuteAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count is 0 or > MaxProjects)
            return TaskErrors.InvalidProjectIds;

        Guid[] ids = [.. projectIds.Distinct()];
        List<ProjectTaskCounts> counts = await _db.TaskItems
            .Where(t => t.ProjectId != null && ids.Contains(t.ProjectId.Value))
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new ProjectTaskCounts(g.Key, g.Count(), g.Count(t => t.CompletedAt != null)))
            .ToListAsync(ct);

        return new TaskCountsResponse(counts);
    }
}
