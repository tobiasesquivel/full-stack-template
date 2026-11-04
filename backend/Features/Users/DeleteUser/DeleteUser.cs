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
    private readonly IAuthModule _auth;

    public DeleteUser(AppDbContext db, IAuthModule auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _auth.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        ErrorOr<Success> deleted = await _auth.DeleteAccountAsync(authId, ct);
        if (deleted.IsError && deleted.FirstError.Type == ErrorType.NotFound)
            return UserErrors.NotFound(authId);

        return deleted;
    }
}
