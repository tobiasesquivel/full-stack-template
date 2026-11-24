namespace TemplateApp.Features.Projects.UpdateProject;

internal sealed record UpdateProjectCommand(string CallerId, Guid Id, string Name, bool IsArchived, uint? Version);
