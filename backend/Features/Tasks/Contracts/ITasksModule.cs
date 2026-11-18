namespace TemplateApp.Features.Tasks.Contracts;

public interface ITasksModule
{
    Task DeleteOwnedTasksAsync(string ownerId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, TaskCountsSummary>> CountByProjectAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct);
}
