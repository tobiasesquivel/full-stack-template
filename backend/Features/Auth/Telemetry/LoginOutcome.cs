namespace TemplateApp.Features.Auth.Telemetry;

internal enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
}
