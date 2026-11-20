namespace TemplateApp.Features.Auth.DecideAccountClosure;

internal sealed record OwnerTasksHeldIntegrationEvent(Guid ClosureId, string UserId, DateTimeOffset HeldAt);
