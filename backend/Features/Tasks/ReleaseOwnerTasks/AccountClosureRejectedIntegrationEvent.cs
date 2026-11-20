namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

internal sealed record AccountClosureRejectedIntegrationEvent(Guid ClosureId, string UserId, string Reason, DateTimeOffset RejectedAt);
