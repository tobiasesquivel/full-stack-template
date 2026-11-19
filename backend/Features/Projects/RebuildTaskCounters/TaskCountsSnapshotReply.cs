namespace TemplateApp.Features.Projects.RebuildTaskCounters;

internal sealed record TaskCountsSnapshotReply(IReadOnlyList<SnapshotCountsItem> Counts, IReadOnlyList<Guid> UndeliveredMessageIds);
