namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public sealed record OwnerTasksHeldIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset HeldAt);
