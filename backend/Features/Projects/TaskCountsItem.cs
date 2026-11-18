namespace TemplateApp.Features.Projects;

internal sealed record TaskCountsItem(Guid ProjectId, int Total, int Completed);
