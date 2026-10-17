using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Domain;

public sealed class UserProfile : IOwnedResource
{
    public const int DisplayNameMaxLength = 200;

    public required string AuthId { get; init; }
    public required string DisplayName { get; set; }
    public string Timezone { get; set; } = "UTC";

    public string OwnerId => AuthId;

    public static bool IsValidDisplayName(string displayName) =>
        NormalizeDisplayName(displayName).Length is >= 1 and <= DisplayNameMaxLength;

    public static string NormalizeDisplayName(string displayName) => displayName.Trim();
}
