namespace TemplateApp.Features.Auth;

internal enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
}
