using System.Text.Json;

namespace TemplateApp.Tests.Support;

public sealed class OutboxRow
{
    public long Id { get; init; }
    public Guid MessageId { get; init; }
    public required string Type { get; init; }
    public int Version { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? TraceParent { get; init; }
    public int Attempts { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public DateTimeOffset? FailedAt { get; init; }

    public JsonElement Event
    {
        get
        {
            using JsonDocument document = JsonDocument.Parse(Payload);
            return document.RootElement.Clone();
        }
    }
}
