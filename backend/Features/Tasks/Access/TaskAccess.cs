using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class TaskAccess
{
    private HashSet<string> _permissions = new(StringComparer.Ordinal);

    public void Grant(IEnumerable<string> permissions) => _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);

    public bool HasPermission(string permission) => _permissions.Contains(permission);

    public bool CanAccess(string callerId, IOwnedResource resource, string permission) =>
        string.Equals(callerId, resource.OwnerId, StringComparison.Ordinal) || HasPermission(permission);
}
