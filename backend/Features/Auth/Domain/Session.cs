namespace TemplateApp.Features.Auth.Domain;

public sealed class Session
{
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }

    public static Session Open(string userId, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        UserId = userId,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
        LastSeenAt = now,
    };

    public bool IsActive(DateTimeOffset now, TimeSpan idleTimeout) =>
        ExpiresAt > now && LastSeenAt + idleTimeout > now;

    public bool NeedsTouch(DateTimeOffset now) => now - LastSeenAt >= TouchInterval;
}
