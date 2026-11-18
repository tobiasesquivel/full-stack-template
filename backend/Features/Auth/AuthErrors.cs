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

    public static readonly Error AccountNotFound = Error.NotFound(
        code: "Auth.AccountNotFound",
        description: "Account not found.");

    public static readonly Error SessionInactive = Error.NotFound(
        code: "Auth.SessionInactive",
        description: "The session does not exist, expired or belongs to another user.");

    public static readonly Error InvalidUserName = Error.Validation(
        code: "UserName",
        description: "UserName must be between 1 and 100 characters.");

    public static readonly Error InvalidEmail = Error.Validation(
        code: "Email",
        description: "Email must be a valid email address.");

    public static readonly Error InvalidPassword = Error.Validation(
        code: "Password",
        description: "Password must be between 6 and 128 characters.");

    public static Error IdentityRejected(IdentityError error) => Error.Validation(
        code: error.Code,
        description: error.Description);

    public static Error AccountDeleteFailed(IdentityError error) => Error.Failure(
        code: "Auth.AccountDeleteFailed",
        description: error.Description);
}
