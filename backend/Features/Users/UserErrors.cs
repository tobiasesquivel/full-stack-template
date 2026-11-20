using ErrorOr;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users;

internal static class UserErrors
{
    public static Error NotFound(string authId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{authId}' not found.");

    public static Error ClosureNotFound(Guid closureId) => Error.NotFound(
        code: "User.ClosureNotFound",
        description: $"Account closure '{closureId}' not found.");

    public static Error ClosureNotCancellable(Guid closureId) => Error.Conflict(
        code: "User.ClosureNotCancellable",
        description: $"Account closure '{closureId}' already started or ended: only a scheduled closure can be cancelled.");

    public static readonly Error Forbidden = Error.Forbidden(
        code: "User.Forbidden",
        description: "Access denied.");

    public static readonly Error InvalidDisplayName = Error.Validation(
        code: "DisplayName",
        description: $"DisplayName must be between 1 and {UserProfile.DisplayNameMaxLength} characters.");
}
