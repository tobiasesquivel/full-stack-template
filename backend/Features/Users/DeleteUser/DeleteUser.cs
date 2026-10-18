using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.DeleteUser;

public sealed class DeleteUser
{
    private readonly AppDbContext _db;
    private readonly IAccessPolicy _access;
    private readonly IUserAccounts _accounts;

    public DeleteUser(AppDbContext db, IAccessPolicy access, IUserAccounts accounts)
    {
        _db = db;
        _access = access;
        _accounts = accounts;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _access.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        ErrorOr<Success> deleted = await _accounts.DeleteAsync(authId, ct);
        if (deleted.IsError && deleted.FirstError.Type == ErrorType.NotFound)
            return UserErrors.NotFound(authId);

        return deleted;
    }
}
