namespace TemplateApp.Infrastructure.Messaging;

public sealed class OutboxMessage
{
    public long Id { get; init; }
    public Guid MessageId { get; init; }
    public required string Type { get; init; }
    public int Version { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? TraceParent { get; init; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
}
