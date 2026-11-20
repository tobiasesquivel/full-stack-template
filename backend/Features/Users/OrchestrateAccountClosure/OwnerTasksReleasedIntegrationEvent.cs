namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public sealed record OwnerTasksReleasedIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset ReleasedAt);
