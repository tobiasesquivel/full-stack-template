namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

internal sealed record SnapshotCountsItem(Guid ProjectId, int Total, int Completed);
