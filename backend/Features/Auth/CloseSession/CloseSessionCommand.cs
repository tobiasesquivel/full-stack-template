namespace TemplateApp.Features.Auth.CloseSession;

internal sealed record CloseSessionCommand(Guid? SessionId);
