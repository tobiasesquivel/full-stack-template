using Wolverine;
using Wolverine.Persistence.Sagas;

namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public sealed partial class AccountClosureGracePeriod : Saga
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public int RemindersSent { get; set; }
    public int Version { get; set; }

    internal static (AccountClosureGracePeriod Saga, object[] Messages) Open(
        Guid closureId,
        string userId,
        DateTimeOffset now,
        TimeSpan gracePeriod,
        IReadOnlyList<TimeSpan> reminders) =>
        (new AccountClosureGracePeriod
        {
            Id = closureId,
            UserId = userId,
            RequestedAt = now,
            ClosesAt = now + gracePeriod,
        },
        [.. reminders.Select((after, index) => new AccountClosureReminderDue(closureId, index + 1, after)), new AccountClosureGraceExpired(closureId, gracePeriod)]);

    public async Task Handle(
        [SagaIdentityFrom(nameof(AccountClosureReminderDue.ClosureId))] AccountClosureReminderDue reminder,
        IAccountClosureGraceSteps steps,
        CancellationToken ct)
    {
        AccountClosureTrace.Mark(Id);
        if (await steps.RemindAsync(Id, UserId, reminder.Number, ClosesAt, ct))
            RemindersSent++;
    }

    public async Task<OutgoingMessages> Handle(
        [SagaIdentityFrom(nameof(AccountClosureGraceExpired.ClosureId))] AccountClosureGraceExpired expired,
        IAccountClosureGraceSteps steps,
        ILogger<AccountClosureGracePeriod> logger,
        CancellationToken ct)
    {
        AccountClosureTrace.Mark(Id);
        MarkCompleted();
        OutgoingMessages messages = [.. await steps.StartClosureAsync(Id, UserId, ct)];
        LogExpired(logger, Id, expired.After, messages.Count > 0);
        return messages;
    }

    public static void NotFound(AccountClosureReminderDue reminder)
    {
    }

    public static void NotFound(AccountClosureGraceExpired expired)
    {
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: the grace period of {After} ended; the closure started: {Started}")]
    private static partial void LogExpired(ILogger logger, Guid closureId, TimeSpan after, bool started);
}
