namespace TemplateApp.Features.Users.GetAccountClosure;

internal sealed record GetAccountClosureCommand(string CallerId, Guid ClosureId);
