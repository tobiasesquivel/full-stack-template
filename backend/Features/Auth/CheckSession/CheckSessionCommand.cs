namespace TemplateApp.Features.Auth.CheckSession;

internal sealed partial record CheckSessionCommand(string UserId, Guid SessionId);
