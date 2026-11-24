namespace TemplateApp.Features.Projects.GetProject;

internal sealed record GetProjectCommand(string CallerId, Guid Id);
