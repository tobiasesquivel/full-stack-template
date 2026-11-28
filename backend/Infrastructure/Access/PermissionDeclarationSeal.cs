using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace TemplateApp.Infrastructure.Access;

public static class PermissionDeclarationSeal
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const string Purpose = "TemplateApp.PermissionDeclaration.v1";

    public static string Seal(IDataProtectionProvider keys, FeaturePermissions declaration) =>
        Seal(keys, declaration, DateTimeOffset.UtcNow + Lifetime);

    public static string Seal(IDataProtectionProvider keys, FeaturePermissions declaration, DateTimeOffset expiresAt) =>
        Protector(keys).Protect(JsonSerializer.Serialize(declaration, JsonSerializerOptions.Web), expiresAt);

    public static FeaturePermissions? Open(IDataProtectionProvider keys, string? seal)
    {
        if (string.IsNullOrEmpty(seal))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FeaturePermissions>(Protector(keys).Unprotect(seal), JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider keys) =>
        keys.CreateProtector(Purpose).ToTimeLimitedDataProtector();
}
