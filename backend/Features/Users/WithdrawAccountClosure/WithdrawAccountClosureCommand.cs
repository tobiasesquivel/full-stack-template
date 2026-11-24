namespace TemplateApp.Features.Users.WithdrawAccountClosure;

internal sealed record WithdrawAccountClosureCommand(string CallerId, Guid ClosureId);
