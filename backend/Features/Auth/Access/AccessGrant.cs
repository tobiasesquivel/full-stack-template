namespace TemplateApp.Features.Auth.Access;

public sealed record AccessGrant(IReadOnlyList<string> Roles, IReadOnlySet<string> Permissions)
{
    public static AccessGrant None { get; } = new([], new HashSet<string>(StringComparer.Ordinal));
}
