using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.DeleteUser;

public sealed class DeleteUser
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;

    public DeleteUser(UserManager<AppIdentityUser> users, AppDbContext db, IAccessPolicy access)
    {
        _users = users;
        _db = db;
        _access = access;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _access.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return Error.Forbidden(code: "User.Forbidden", description: "Access denied.");

        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        if (identity is null)
            return UserErrors.NotFound(authId);

        IdentityResult deleted = await _users.DeleteAsync(identity);
        if (!deleted.Succeeded)
            return deleted.Errors
                .Select(e => Error.Failure(code: e.Code, description: e.Description))
                .ToList();

        return Result.Success;
    }
}
