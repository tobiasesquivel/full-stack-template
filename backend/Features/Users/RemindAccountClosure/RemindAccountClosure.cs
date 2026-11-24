using System.Globalization;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Features.Users.Telemetry;
using TemplateApp.Infrastructure.Persistence.Transactions;

namespace TemplateApp.Features.Users.RemindAccountClosure;

internal sealed partial class RemindAccountClosure
{
    private readonly AccountClosureHistory _history;
    private readonly UserMetrics _metrics;
    private readonly AfterCommit _afterCommit;
    private readonly TimeProvider _clock;
    private readonly ILogger<RemindAccountClosure> _logger;

    public RemindAccountClosure(
        AccountClosureHistory history,
        UserMetrics metrics,
        AfterCommit afterCommit,
        TimeProvider clock,
        ILogger<RemindAccountClosure> logger)
    {
        _history = history;
        _metrics = metrics;
        _afterCommit = afterCommit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> RemindAsync(Guid closureId, string userId, int number, DateTimeOffset closesAt, CancellationToken ct)
    {
        string detail = number.ToString(CultureInfo.InvariantCulture);
        if (!await _history.RecordAsync(closureId, userId, AccountClosureFacts.ReminderSent, AccountClosureFacts.SystemActor, detail, _clock.GetUtcNow(), ct))
            return false;

        _afterCommit.Add(() =>
        {
            _metrics.AccountClosureReminded();
            LogReminded(closureId, userId, number, closesAt);
        });
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: reminder {Number} for user {UserId}, the account closes at {ClosesAt}")]
    private partial void LogReminded(Guid closureId, string userId, int number, DateTimeOffset closesAt);
}
