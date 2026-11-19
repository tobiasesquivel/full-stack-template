using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Projects;

internal sealed class ProjectResponses
{
    private static readonly TaskCounts NoTasks = new(0, 0);

    private readonly ProjectsDbContext _db;
    private readonly IUsersModule _users;

    public ProjectResponses(ProjectsDbContext db, IUsersModule users)
    {
        _db = db;
        _users = users;
    }

    public async Task<ProjectResponse> ComposeAsync(ProjectRow row, CancellationToken ct) =>
        (await ComposeAsync([row], ct))[0];

    public async Task<IReadOnlyList<ProjectResponse>> ComposeAsync(IReadOnlyList<ProjectRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        Guid[] ids = [.. rows.Select(r => r.Id)];
        Dictionary<Guid, TaskCounts> counters = await _db.TaskCounters
            .Where(c => ids.Contains(c.ProjectId))
            .Select(c => new { c.ProjectId, Counts = new TaskCounts(c.TaskCount, c.CompletedTaskCount) })
            .ToDictionaryAsync(c => c.ProjectId, c => c.Counts, ct);
        IReadOnlyDictionary<string, string> ownerNames = await _users.FindDisplayNamesAsync(
            [.. rows.Select(r => r.OwnerId).Distinct(StringComparer.Ordinal)],
            ct);

        return
        [
            .. rows.Select(row =>
            {
                TaskCounts tasks = counters.GetValueOrDefault(row.Id, NoTasks);
                int total = Math.Max(0, tasks.Total);
                int completed = Math.Clamp(tasks.Completed, 0, total);
                return new ProjectResponse(
                    row.Id,
                    row.Name,
                    row.IsArchived,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.Version,
                    ownerNames.GetValueOrDefault(row.OwnerId, string.Empty),
                    total,
                    completed);
            }),
        ];
    }
}
