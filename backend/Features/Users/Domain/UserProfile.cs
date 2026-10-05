using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Domain;

public sealed class UserProfile : IOwnedResource
{
    public required string AuthId { get; init; }
    public required string DisplayName { get; set; }
    public string Timezone { get; set; } = "UTC";

    public string OwnerId => AuthId;
}
