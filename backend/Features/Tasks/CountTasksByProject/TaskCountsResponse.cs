namespace TemplateApp.Features.Tasks.CountTasksByProject;

internal sealed record TaskCountsResponse(IReadOnlyList<ProjectTaskCounts> Counts);
