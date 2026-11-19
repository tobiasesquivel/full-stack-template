namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed record TaskDeletedIntegrationEvent(Guid TaskId, string OwnerId, Guid? ProjectId, bool WasCompleted);
