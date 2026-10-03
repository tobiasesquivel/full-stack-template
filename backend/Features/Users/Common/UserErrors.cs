using ErrorOr;

namespace TemplateApp.Features.Users.Common;

public static class UserErrors
{
    public static Error NotFound(string authId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{authId}' not found.");
}
