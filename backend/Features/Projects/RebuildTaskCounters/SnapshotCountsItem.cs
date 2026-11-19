namespace TemplateApp.Features.Projects.RebuildTaskCounters;

internal sealed record SnapshotCountsItem(Guid ProjectId, int Total, int Completed);
