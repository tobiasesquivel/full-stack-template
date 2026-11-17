namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed record TaskListResponse(IReadOnlyList<TaskResponse> Items, string? NextCursor);
