namespace TemplateApp.Features.Users.UpdateProfile;

internal sealed partial record UpdateProfileCommand(string CallerId, string DisplayName, string Timezone);
