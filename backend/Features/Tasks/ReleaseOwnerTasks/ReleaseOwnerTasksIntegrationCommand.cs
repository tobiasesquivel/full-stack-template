namespace TemplateApp.Features.Tasks.ReleaseOwnerTasks;

internal sealed record ReleaseOwnerTasksIntegrationCommand(Guid ClosureId, string UserId);
