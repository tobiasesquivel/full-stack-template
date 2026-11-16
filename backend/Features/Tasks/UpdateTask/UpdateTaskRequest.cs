namespace TemplateApp.Features.Tasks.UpdateTask;

public sealed record UpdateTaskRequest(string Title, bool IsCompleted, uint? Version);
