using System.Transactions;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Projects.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.DeleteUser;

internal sealed class DeleteUser
{
    private readonly UsersDbContext _db;
    private readonly IAuthModule _auth;
    private readonly ITasksModule _tasks;
    private readonly IProjectsModule _projects;
    private readonly IdempotencyKeys _keys;

    public DeleteUser(UsersDbContext db, IAuthModule auth, ITasksModule tasks, IProjectsModule projects, IdempotencyKeys keys)
    {
        _db = db;
        _auth = auth;
        _tasks = tasks;
        _projects = projects;
        _keys = keys;
    }

    public async Task<ErrorOr<Success>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        using TransactionScope transaction = CrossContextTransaction.Begin();

        UserProfile? profile = await _db.UserProfiles
            .SingleOrDefaultAsync(p => p.AuthId == authId, ct);
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _auth.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        await _tasks.DeleteOwnedTasksAsync(authId, ct);
        await _projects.DeleteOwnedProjectsAsync(authId, ct);
        await _keys.ForgetOwnerAsync(authId, ct);
        _db.UserProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);

        ErrorOr<Success> deleted = await _auth.DeleteAccountAsync(authId, ct);
        if (deleted.IsError)
            return deleted.FirstError.Type == ErrorType.NotFound ? UserErrors.NotFound(authId) : deleted;

        transaction.Complete();
        return deleted;
    }
}
