namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal sealed record GetAccountClosureHistoryCommand(string CallerId, Guid ClosureId);
