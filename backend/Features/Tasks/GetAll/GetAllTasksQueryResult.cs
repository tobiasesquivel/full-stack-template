using TemplateApp.Features.Tasks.Common;

namespace TemplateApp.Features.Tasks.GetAll;

public sealed record GetAllTasksQueryResult(IReadOnlyList<TaskItemResponse> Tasks);
