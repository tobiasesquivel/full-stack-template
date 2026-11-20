using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Users.DeleteUser;

internal sealed partial class DeleteUser
{
    private readonly UsersDbContext _db;
    private readonly SagaOutbox<UsersDbContext> _sagas;
    private readonly IAuthModule _auth;
    private readonly AccountClosureOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeleteUser> _logger;

    public DeleteUser(
        UsersDbContext db,
        SagaOutbox<UsersDbContext> sagas,
        IAuthModule auth,
        IOptions<AccountClosureOptions> options,
        TimeProvider clock,
        ILogger<DeleteUser> logger)
    {
        _db = db;
        _sagas = sagas;
        _auth = auth;
        _options = options.Value;
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
        Guid closureId = Guid.CreateVersion7(now);
        if (!profile.RequestClosure(closureId, now))
        {
            await transaction.CommitAsync(ct);
            return new AccountClosureResponse(profile.ClosureId!.Value, "closing");
        }

        (AccountClosureSaga saga, object[] messages) = AccountClosureSaga.Open(closureId, authId, now, _options.OverdueAfter);
        await _sagas.StartAndCommitAsync(saga, messages, ct);
        AccountClosureTrace.Mark(closureId);
        LogRequested(closureId, authId);
        return new AccountClosureResponse(closureId, "closing");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: Users marks user {UserId} as closing and the orchestrator asks Tasks to hold the tasks")]
    private partial void LogRequested(Guid closureId, string userId);
}
