namespace TemplateApp.Features.Tasks.DeleteOwnedTasks;

internal sealed record UserDeletedIntegrationEvent(string UserId, DateTimeOffset DeletedAt);
