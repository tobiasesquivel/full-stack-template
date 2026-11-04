namespace TemplateApp.Features.Tasks.ListTasks;

public sealed record TaskListResponse(IReadOnlyList<TaskResponse> Items, string? NextCursor);
