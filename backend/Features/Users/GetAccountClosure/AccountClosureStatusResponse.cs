namespace TemplateApp.Features.Users.GetAccountClosure;

internal sealed record AccountClosureStatusResponse(
    Guid ClosureId,
    string UserId,
    string State,
    string? Reason,
    bool Overdue,
    DateTimeOffset StartedAt,
    DateTimeOffset StateChangedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? ClosesAt);
