namespace TemplateApp.Infrastructure.Persistence.Audit;

public sealed class AuditEntry
{
    public long Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public required string ActorKind { get; init; }
    public string? ActorId { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string Operation { get; init; }
    public required string[] ChangedProperties { get; init; }
    public string? TraceId { get; init; }
}
