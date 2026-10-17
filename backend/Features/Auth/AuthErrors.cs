using ErrorOr;

namespace TemplateApp.Features.Auth;

public static class AuthErrors
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

    public static readonly Error InvalidSession = Error.Unauthorized(
        code: "Auth.InvalidSession",
        description: "Invalid session.");

    public static readonly Error SessionRevoked = Error.Unauthorized(
        code: "Auth.SessionRevoked",
        description: "Session has been revoked.");
}
