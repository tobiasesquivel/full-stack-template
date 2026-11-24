namespace TemplateApp.Features.Auth.MessageNames;

internal static class AuthEvents
{
    public const string Exchange = "auth.account-events";
    public const string AccountDeleted = "auth.account-deleted";
    public const string AccountClosureRejected = "auth.account-closure-rejected";
    public const int Version = 1;
}
