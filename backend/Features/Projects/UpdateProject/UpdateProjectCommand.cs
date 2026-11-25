namespace TemplateApp.Features.Projects.UpdateProject;

internal sealed partial record UpdateProjectCommand(string CallerId, Guid Id, string Name, bool IsArchived, uint? Version);
