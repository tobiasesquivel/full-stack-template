namespace TemplateApp.Features.Users.GetAccountClosureHistory;

internal sealed record AccountClosureFactResponse(string Kind, DateTimeOffset OccurredAt, string Actor, string? Detail);
