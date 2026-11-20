namespace TemplateApp.Features.Projects.ApplyTaskEvent;

internal sealed record TaskDeletedIntegrationEvent(Guid? ProjectId, bool WasCompleted);
