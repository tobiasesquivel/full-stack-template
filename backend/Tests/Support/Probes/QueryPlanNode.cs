namespace TemplateApp.Tests.Support.Probes;

internal sealed record QueryPlanNode(
    string NodeType,
    string? IndexName,
    string? ScanDirection,
    string? IndexCondition,
    double ActualRows,
    long SharedBlocks);
