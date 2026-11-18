namespace TemplateApp.Features.Projects.UpdateProject;

internal sealed record UpdateProjectRequest(string Name, bool IsArchived, uint? Version);
