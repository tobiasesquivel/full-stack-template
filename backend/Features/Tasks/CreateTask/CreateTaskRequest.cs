namespace TemplateApp.Features.Tasks.CreateTask;

internal sealed record CreateTaskRequest(string Title, Guid? ProjectId = null);
