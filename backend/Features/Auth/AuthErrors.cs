using ErrorOr;
using Microsoft.AspNetCore.Identity;

namespace TemplateApp.Features.Auth;

internal static class AuthErrors
{
    public static readonly Error EmailAlreadyExists = Error.Conflict(
        code: "Auth.EmailAlreadyExists",
        description: "Email is already registered.");

    public static readonly Error UserNameAlreadyExists = Error.Conflict(
        code: "Auth.UserNameAlreadyExists",
        description: "User name is already registered.");

    public static readonly Error InvalidCredentials = Error.Unauthorized(
        code: "Auth.InvalidCredentials",
        description: "Invalid credentials.");

    public static readonly Error ProfileMissing = Error.Failure(
        code: "Auth.ProfileMissing",
        description: "User profile is missing.");

    public static readonly Error LastSuperAdmin = Error.Conflict(
        code: "Auth.LastSuperAdmin",
        description: "The only SuperAdmin account cannot be closed.");

    public static readonly Error SessionInactive = Error.NotFound(
        code: "Auth.SessionInactive",
        description: "The session does not exist, expired or belongs to another user.");

    public static Error IdentityRejected(IdentityError error) => Error.Validation(
        code: error.Code,
        description: error.Description);
}
