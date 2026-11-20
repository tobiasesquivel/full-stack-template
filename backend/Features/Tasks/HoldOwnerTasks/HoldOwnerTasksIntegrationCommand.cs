namespace TemplateApp.Features.Tasks.HoldOwnerTasks;

internal sealed record HoldOwnerTasksIntegrationCommand(Guid ClosureId, string UserId, DateTimeOffset RequestedAt);
