using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Users.Contracts;

namespace TemplateApp.Features.Projects;

internal sealed class ProjectResponses
{
    private static readonly TaskCountsSummary NoTasks = new(0, 0);

    private readonly ITasksModule _tasks;
    private readonly IUsersModule _users;

    public ProjectResponses(ITasksModule tasks, IUsersModule users)
    {
        _tasks = tasks;
        _users = users;
    }

    public async Task<ProjectResponse> ComposeAsync(ProjectRow row, CancellationToken ct) =>
        (await ComposeAsync([row], ct))[0];

    public async Task<IReadOnlyList<ProjectResponse>> ComposeAsync(IReadOnlyList<ProjectRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        IReadOnlyDictionary<Guid, TaskCountsSummary> counts = await _tasks.CountByProjectAsync([.. rows.Select(r => r.Id)], ct);
        IReadOnlyDictionary<string, string> ownerNames = await _users.FindDisplayNamesAsync(
            [.. rows.Select(r => r.OwnerId).Distinct(StringComparer.Ordinal)],
            ct);

        return
        [
            .. rows.Select(row =>
            {
                TaskCountsSummary tasks = counts.GetValueOrDefault(row.Id, NoTasks);
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
