namespace TemplateApp.Features.Tasks.HoldOwnerTasks;

internal sealed record AccountClosureRequestedIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset RequestedAt);
