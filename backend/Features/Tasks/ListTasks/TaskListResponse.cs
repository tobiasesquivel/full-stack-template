namespace TemplateApp.Features.Tasks.ListTasks;

public sealed record TaskListResponse(IReadOnlyList<TaskResponse> Items, int TotalCount, int Page, int PageSize);
