namespace TemplateApp.Features.Tasks.Access;

internal sealed record SessionCheckRequest(string UserId, Guid SessionId);
