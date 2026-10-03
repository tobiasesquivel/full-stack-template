using ErrorOr;

namespace TemplateApp.Features.Auth.Common;

public static class AuthErrors
{
    public static readonly Error EmailAlreadyExists = Error.Conflict(
        code: "Auth.EmailAlreadyExists",
        description: "Email is already registered.");

    public static readonly Error InvalidCredentials = Error.Unauthorized(
        code: "Auth.InvalidCredentials",
        description: "Invalid credentials.");

    public static readonly Error UserNotFound = Error.NotFound(
        code: "Auth.UserNotFound",
        description: "User not found.");

    public static readonly Error InvalidToken = Error.Unauthorized(
        code: "Auth.InvalidToken",
        description: "Invalid refresh token.");

    public static readonly Error TokenTheft = Error.Unauthorized(
        code: "Auth.TokenTheft",
        description: "Token theft detected. All sessions revoked.");
}
