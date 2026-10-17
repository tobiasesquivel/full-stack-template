using ErrorOr;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.GetUser;

public sealed class GetUser
{
    private readonly UserManager<AppIdentityUser> _users;
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;
    private readonly UserResponseBuilder _responses;

    public GetUser(
        UserManager<AppIdentityUser> users,
        AppDbContext db,
        IAccessPolicy access,
        UserResponseBuilder responses)
    {
        _users = users;
        _db = db;
        _access = access;
        _responses = responses;
    }

    public async Task<ErrorOr<UserResponse>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _access.CanAccessAsync(callerId, profile, UserPermissions.Read, ct))
            return Error.Forbidden(code: "User.Forbidden", description: "Access denied.");

        AppIdentityUser? identity = await _users.FindByIdAsync(authId);
        if (identity is null)
            return UserErrors.NotFound(authId);

        return await _responses.BuildAsync(identity, profile, ct);
    }
}
