namespace TemplateApp.Features.Projects;

internal sealed record TaskCountsReply(IReadOnlyList<TaskCountsItem> Counts);
