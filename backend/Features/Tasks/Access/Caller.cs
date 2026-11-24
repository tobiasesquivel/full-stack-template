using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Access;

internal sealed record Caller(string Id, IReadOnlySet<string> Permissions)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public bool CanAccess(IOwnedResource resource, string permission) =>
        string.Equals(Id, resource.OwnerId, StringComparison.Ordinal) || HasPermission(permission);
}
