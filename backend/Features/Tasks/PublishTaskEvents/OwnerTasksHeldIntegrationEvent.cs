namespace TemplateApp.Features.Tasks.PublishTaskEvents;

internal sealed record OwnerTasksHeldIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset HeldAt);
