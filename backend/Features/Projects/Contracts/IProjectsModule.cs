namespace TemplateApp.Features.Projects.Contracts;

public interface IProjectsModule
{
    Task<ProjectAssignmentInfo?> FindAssignableProjectAsync(string callerId, Guid projectId, CancellationToken ct);

    Task DeleteOwnedProjectsAsync(string ownerId, CancellationToken ct);
}
