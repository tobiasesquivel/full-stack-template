namespace TemplateApp.Features.Tasks.UpdateTask;

internal sealed record UpdateTaskRequest(string Title, bool IsCompleted, uint? Version);
