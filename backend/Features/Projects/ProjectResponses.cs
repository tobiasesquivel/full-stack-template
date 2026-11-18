using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Projects;

internal sealed class ProjectResponses
{
    private static readonly TaskCounts NoTasks = new(0, 0);

    private readonly TaskCountsClient _tasks;
    private readonly IUsersModule _users;

    public ProjectResponses(TaskCountsClient tasks, IUsersModule users)
    {
        _tasks = tasks;
        _users = users;
    }

    public async Task<ProjectResponse> ComposeAsync(ProjectRow row, CancellationToken ct) =>
        (await ComposeAsync([row], ct))[0];

    public async Task<ProjectResponse> ComposeNewAsync(ProjectRow row, CancellationToken ct) =>
        (await ComposeAsync([row], new Dictionary<Guid, TaskCounts>(), ct))[0];

    public async Task<IReadOnlyList<ProjectResponse>> ComposeAsync(IReadOnlyList<ProjectRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        return await ComposeAsync(rows, await _tasks.CountByProjectAsync([.. rows.Select(r => r.Id)], ct), ct);
    }

    private async Task<IReadOnlyList<ProjectResponse>> ComposeAsync(
        IReadOnlyList<ProjectRow> rows,
        IReadOnlyDictionary<Guid, TaskCounts> counts,
        CancellationToken ct)
    {
        IReadOnlyDictionary<string, string> ownerNames = await _users.FindDisplayNamesAsync(
            [.. rows.Select(r => r.OwnerId).Distinct(StringComparer.Ordinal)],
            ct);

        return
        [
            .. rows.Select(row =>
            {
                TaskCounts tasks = counts.GetValueOrDefault(row.Id, NoTasks);
                return new ProjectResponse(
                    row.Id,
                    row.Name,
                    row.IsArchived,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.Version,
                    ownerNames.GetValueOrDefault(row.OwnerId, string.Empty),
                    tasks.Total,
                    tasks.Completed);
            }),
        ];
    }
}
