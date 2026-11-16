namespace TemplateApp.Infrastructure.Persistence;

public sealed class IdempotencyKey
{
    public required string OwnerId { get; init; }
    public Guid Key { get; init; }
    public required byte[] Fingerprint { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int? StatusCode { get; init; }
    public string? Location { get; init; }
    public string? ContentType { get; init; }
    public byte[]? Body { get; init; }
}
