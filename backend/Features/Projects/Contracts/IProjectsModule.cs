namespace TemplateApp.Features.Projects.Contracts;

public interface IProjectsModule
{
    Task DeleteOwnedProjectsAsync(string ownerId, CancellationToken ct);
}
