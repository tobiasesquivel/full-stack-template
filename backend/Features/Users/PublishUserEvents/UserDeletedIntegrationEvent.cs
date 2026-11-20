namespace TemplateApp.Features.Users.PublishUserEvents;

internal sealed record UserDeletedIntegrationEvent(string UserId, DateTimeOffset DeletedAt);
