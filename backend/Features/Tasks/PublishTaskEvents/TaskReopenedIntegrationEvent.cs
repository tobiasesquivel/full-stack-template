namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed record TaskReopenedIntegrationEvent(Guid TaskId, string OwnerId, Guid? ProjectId, DateTimeOffset ReopenedAt);
