namespace TemplateApp.Features.Tasks.Contracts;

public sealed record TaskListResponse(IReadOnlyList<TaskResponse> Items, int TotalCount, int Page, int PageSize);
