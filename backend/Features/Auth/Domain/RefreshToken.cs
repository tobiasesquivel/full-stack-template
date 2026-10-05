using System.Security.Cryptography;
using System.Text;

namespace TemplateApp.Features.Auth.Domain;

public sealed class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public required string TokenHash { get; init; }
    public required Guid FamilyId { get; init; }
    public required string UserId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, string? replacedByTokenHash = null)
    {
        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
    }

    public static string Hash(string plaintext)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
    }

    public static string NewPlaintext()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
