namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed record OwnerTasksReleasedIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset ReleasedAt);
