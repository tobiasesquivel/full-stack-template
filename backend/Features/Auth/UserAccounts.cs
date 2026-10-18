using ErrorOr;
using Microsoft.AspNetCore.Identity;
using TemplateApp.Features.Auth.Contracts;

namespace TemplateApp.Features.Auth;

public sealed class UserAccounts : IUserAccounts
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly IAccessPolicy _access;

    public UserAccounts(UserManager<AppIdentityUser> users, IAccessPolicy access)
    {
        _users = users;
        _access = access;
    }

    public async Task<UserAccount?> FindAsync(string authId, CancellationToken ct)
    {
        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        return identity is null ? null : await DescribeAsync(identity, ct);
    }

    public async Task<UserAccount> DescribeAsync(AppIdentityUser identity, CancellationToken ct)
    {
        IList<string> roles = await _users.GetRolesAsync(identity);
        IReadOnlySet<string> permissions = await _access.GetPermissionsAsync(identity.Id, ct);

        return new UserAccount(
            identity.Id,
            identity.UserName,
            identity.Email,
            new List<string>(roles).AsReadOnly(),
            permissions.ToList().AsReadOnly());
    }

    public async Task<ErrorOr<Success>> DeleteAsync(string authId, CancellationToken ct)
    {
        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        if (identity is null)
            return AuthErrors.AccountNotFound;

        IdentityResult deleted = await _users.DeleteAsync(identity);
        if (!deleted.Succeeded)
            return deleted.Errors
                .Select(AuthErrors.AccountDeleteFailed)
                .ToList();

        return Result.Success;
    }
}
