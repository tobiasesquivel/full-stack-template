namespace TemplateApp.Features.Auth.CheckSession;

internal sealed record CheckSessionResponse(IReadOnlyList<string> Permissions);
