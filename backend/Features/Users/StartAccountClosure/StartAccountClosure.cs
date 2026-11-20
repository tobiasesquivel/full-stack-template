using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.Persistence;

namespace TemplateApp.Features.Users.StartAccountClosure;

internal sealed partial class StartAccountClosure
{
    private readonly UsersDbContext _db;
    private readonly AccountClosureHistory _history;
    private readonly AccountClosureOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StartAccountClosure> _logger;

    public StartAccountClosure(
        UsersDbContext db,
        AccountClosureHistory history,
        IOptions<AccountClosureOptions> options,
        TimeProvider clock,
        ILogger<StartAccountClosure> logger)
    {
        _db = db;
        _history = history;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<object>> StartAsync(Guid closureId, string userId, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The closure starts inside the transaction of the grace period step that calls it.");

        UserProfile? profile = (await _db.UserProfiles
            .FromSql($"""SELECT * FROM users."UserProfiles" WHERE "AuthId" = {userId} FOR UPDATE""")
            .ToListAsync(ct)).SingleOrDefault();
        DateTimeOffset now = _clock.GetUtcNow();
        if (profile is null || profile.ClosureStatus != AccountClosureStatus.Scheduled || !profile.RequestClosure(closureId, now))
        {
            LogNotScheduled(closureId, userId);
            return [];
        }

        await _history.RecordAsync(closureId, userId, AccountClosureFacts.GraceExpired, AccountClosureFacts.SystemActor, null, now, ct);
        await _history.RecordAsync(closureId, userId, AccountClosureFacts.ClosureStarted, AccountClosureFacts.SystemActor, null, now, ct);
        (AccountClosureSaga saga, object[] messages) = AccountClosureSaga.Open(closureId, userId, now, _options.OverdueAfter);
        _db.AccountClosureSagas.Add(saga);
        LogStarted(closureId, userId);
        return messages;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: the grace period ended, Users marks user {UserId} as closing and the orchestrator asks Tasks to hold the tasks")]
    private partial void LogStarted(Guid closureId, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: the grace period ended but user {UserId} no longer has it scheduled, nothing starts")]
    private partial void LogNotScheduled(Guid closureId, string userId);
}
