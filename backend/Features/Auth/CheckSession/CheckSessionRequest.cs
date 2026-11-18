namespace TemplateApp.Features.Auth.CheckSession;

internal sealed record CheckSessionRequest(string UserId, Guid SessionId);
