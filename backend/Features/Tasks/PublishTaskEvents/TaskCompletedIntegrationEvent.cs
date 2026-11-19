namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed record TaskCompletedIntegrationEvent(Guid TaskId, string OwnerId, Guid? ProjectId, DateTimeOffset CompletedAt);
