namespace TemplateApp.Features.Users.PublishUserEvents;

internal sealed record AccountClosureRequestedIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset RequestedAt);
