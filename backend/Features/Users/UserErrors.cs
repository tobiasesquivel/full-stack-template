using ErrorOr;

namespace TemplateApp.Features.Users;

public static class UserErrors
{
    public static Error NotFound(string authId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{authId}' not found.");

    public static readonly Error InvalidTimezone = Error.Validation(
        code: "Timezone",
        description: "Timezone must be a valid IANA timezone.");
}
