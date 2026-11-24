namespace TemplateApp.Features.Auth.CheckSession;

internal sealed record CheckSessionCommand(string UserId, Guid SessionId);
