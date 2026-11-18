using ErrorOr;

namespace TemplateApp.Shared.Http;

public static class SessionErrors
{
    public static readonly Error SessionRequired = Error.Unauthorized(
        code: "Auth.SessionRequired",
        description: "An active session is required.");

    public static readonly Error AccessDenied = Error.Forbidden(
        code: "Auth.AccessDenied",
        description: "Access denied.");

    public static readonly Error AntiforgeryFailed = Error.Validation(
        code: "Auth.AntiforgeryFailed",
        description: "Invalid antiforgery token.");
}
