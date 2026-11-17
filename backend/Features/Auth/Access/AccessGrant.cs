namespace TemplateApp.Features.Auth.Access;

internal sealed record AccessGrant(IReadOnlyList<string> Roles, IReadOnlySet<string> Permissions)
{
    public static AccessGrant None { get; } = new([], new HashSet<string>(StringComparer.Ordinal));
}
