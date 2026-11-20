using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.DeleteUser;

internal sealed partial class DeleteUser
{
    private readonly UsersDbContext _db;
    private readonly IAuthModule _auth;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeleteUser> _logger;

    public DeleteUser(UsersDbContext db, IAuthModule auth, TimeProvider clock, ILogger<DeleteUser> logger)
    {
        _db = db;
        _auth = auth;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ErrorOr<AccountClosureResponse>> ExecuteAsync(string callerId, string authId, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        UserProfile? profile = (await _db.UserProfiles
            .FromSql($"""SELECT * FROM users."UserProfiles" WHERE "AuthId" = {authId} FOR UPDATE""")
            .ToListAsync(ct)).SingleOrDefault();
        if (profile is null)
            return UserErrors.NotFound(authId);

        if (!await _auth.CanAccessAsync(callerId, profile, UserPermissions.Manage, ct))
            return UserErrors.Forbidden;

        DateTimeOffset now = _clock.GetUtcNow();
        bool requested = profile.RequestClosure(Guid.CreateVersion7(now), now);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        Guid closureId = profile.ClosureId!.Value;
        AccountClosureTrace.Mark(closureId);
        if (requested)
            LogRequested(closureId, authId);

        return new AccountClosureResponse(closureId, "closing");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: Users marks user {UserId} as closing and asks Tasks to hold the tasks")]
    private partial void LogRequested(Guid closureId, string userId);
}
