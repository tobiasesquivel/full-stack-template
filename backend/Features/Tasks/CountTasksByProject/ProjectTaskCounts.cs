namespace TemplateApp.Features.Tasks.CountTasksByProject;

internal sealed record ProjectTaskCounts(Guid ProjectId, int Total, int Completed);
