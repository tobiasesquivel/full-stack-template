using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users;

public sealed class UserResponseBuilder
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly IAccessPolicy _access;

    public UserResponseBuilder(UserManager<AppIdentityUser> users, IAccessPolicy access)
    {
        _users = users;
        _access = access;
    }

    public async Task<UserResponse> BuildAsync(AppIdentityUser identity, UserProfile profile, CancellationToken ct)
    {
        IList<string> roles = await _users.GetRolesAsync(identity);
        IReadOnlySet<string> permissions = await _access.GetPermissionsAsync(identity.Id, ct);

        return new UserResponse(
            identity.Id,
            identity.UserName,
            identity.Email,
            profile.DisplayName,
            profile.Timezone,
            new List<string>(roles).AsReadOnly(),
            permissions.ToList().AsReadOnly());
    }
}
