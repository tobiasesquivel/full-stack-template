namespace TemplateApp.Features.Auth.CloseSession;

internal sealed partial record CloseSessionCommand(Guid? SessionId);
