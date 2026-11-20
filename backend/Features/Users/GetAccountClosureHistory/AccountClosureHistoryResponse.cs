namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal sealed record AccountClosureHistoryResponse(Guid ClosureId, IReadOnlyList<AccountClosureFactResponse> Items);
