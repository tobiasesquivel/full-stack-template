namespace TemplateApp.Features.Tasks.Contracts;

public sealed record UpdateTaskRequest(string Title, bool IsCompleted);
