namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed record TaskCreatedIntegrationEvent(Guid TaskId, string OwnerId, Guid? ProjectId, DateTimeOffset CreatedAt);
