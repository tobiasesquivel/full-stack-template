using ErrorOr;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users;

public static class UserErrors
{
    public static Error NotFound(string authId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{authId}' not found.");

    public static readonly Error Forbidden = Error.Forbidden(
        code: "User.Forbidden",
        description: "Access denied.");

    public static readonly Error InvalidDisplayName = Error.Validation(
        code: "DisplayName",
        description: $"DisplayName must be between 1 and {UserProfile.DisplayNameMaxLength} characters.");
}
