namespace TemplateApp.Tests.Support;

internal sealed record OwnedRows(long Accounts, long Roles, long Sessions, long Profiles, long Tasks, long Projects, long IdempotencyKeys)
{
    public static readonly OwnedRows None = new(0, 0, 0, 0, 0, 0, 0);
}
