namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public sealed record ReleaseOwnerTasksIntegrationCommand(Guid ClosureId, string UserId);
