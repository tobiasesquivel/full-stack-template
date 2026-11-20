namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

internal sealed record HoldOwnerTasksIntegrationCommand(Guid ClosureId, string UserId, DateTimeOffset RequestedAt);
