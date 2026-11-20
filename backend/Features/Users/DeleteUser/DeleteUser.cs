using System.Transactions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.DeleteUser;

internal sealed class DeleteUser
{
    private readonly UsersDbContext _db;
    private readonly IAuthModule _auth;
    private readonly IProjectsModule _projects;
    private readonly IdempotencyKeys _keys;
    private readonly TimeProvider _clock;

    public DeleteUser(UsersDbContext db, IAuthModule auth, IProjectsModule projects, IdempotencyKeys keys, TimeProvider clock)
    {
        _db = db;
        _auth = auth;
        _projects = projects;
        _keys = keys;
        _clock = clock;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _auth.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        using TransactionScope transaction = CrossContextTransaction.Begin();

        await _projects.DeleteOwnedProjectsAsync(authId, ct);
        await _keys.ForgetOwnerAsync(authId, ct);
        profile.MarkDeleted(_clock.GetUtcNow());
        _db.UserProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);

        ErrorOr<Success> deleted = await _auth.DeleteAccountAsync(authId, ct);
        if (deleted.IsError)
            return deleted.FirstError.Type == ErrorType.NotFound ? UserErrors.NotFound(authId) : deleted;

        transaction.Complete();
        return deleted;
    }
}
