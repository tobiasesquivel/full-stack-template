namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed record TaskCreatedIntegrationEvent(Guid? ProjectId);
