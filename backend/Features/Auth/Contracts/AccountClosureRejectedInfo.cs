namespace TemplateApp.Features.Auth.Contracts;

public sealed record AccountClosureRejectedInfo(Guid ClosureId, string UserId, string Reason, DateTimeOffset RejectedAt);
