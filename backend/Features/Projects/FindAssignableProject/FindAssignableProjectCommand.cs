namespace TemplateApp.Features.Projects.FindAssignableProject;

internal sealed record FindAssignableProjectCommand(string OwnerId, Guid ProjectId);
