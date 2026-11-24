using System.Security.Claims;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.Access;

internal sealed class TaskAccess
{
    private HashSet<string> _permissions = new(StringComparer.Ordinal);

    public void Grant(IEnumerable<string> permissions) => _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);

    public Caller CallerOf(ClaimsPrincipal user) => new(user.GetCallerId(), _permissions);
}
