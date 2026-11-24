namespace TemplateApp.Features.Users.UpdateProfile;

internal sealed record UpdateProfileCommand(string CallerId, string DisplayName, string Timezone);
