namespace TemplateApp.Features.Users.WithdrawAccountClosure;

internal sealed partial record WithdrawAccountClosureCommand(string CallerId, Guid ClosureId);
