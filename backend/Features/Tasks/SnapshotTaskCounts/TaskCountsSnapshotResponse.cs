namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

internal sealed record TaskCountsSnapshotResponse(IReadOnlyList<SnapshotCountsItem> Counts, IReadOnlyList<Guid> UndeliveredMessageIds);
