using TemplateApp.Features.Auth.Contracts;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public sealed partial class AccountClosureSaga : Saga
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AccountClosureState State { get; set; }
    public string? Reason { get; set; }
    public bool Overdue { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset StateChangedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int Version { get; set; }

    internal static (AccountClosureSaga Saga, object[] Messages) Open(Guid closureId, string userId, DateTimeOffset now, TimeSpan overdueAfter) =>
        (new AccountClosureSaga
        {
            Id = closureId,
            UserId = userId,
            State = AccountClosureState.HoldingTasks,
            StartedAt = now,
            StateChangedAt = now,
        },
        [new HoldOwnerTasksIntegrationCommand(closureId, userId, now), new AccountClosureOverdue(closureId, overdueAfter)]);

    public DecideAccountClosureInfo? Handle(
        [SagaIdentityFrom(nameof(OwnerTasksHeldIntegrationEvent.ClosureId))] OwnerTasksHeldIntegrationEvent held,
        TimeProvider clock,
        ILogger<AccountClosureSaga> logger) =>
        TryMove(AccountClosureState.Deciding, TaskEvents.OwnerTasksHeld, clock, logger, AccountClosureState.HoldingTasks)
            ? new DecideAccountClosureInfo(Id, UserId)
            : null;

    public async Task Handle(
        [SagaIdentityFrom(nameof(AccountDeletedInfo.ClosureId))] AccountDeletedInfo deleted,
        IAccountClosureSteps steps,
        TimeProvider clock,
        ILogger<AccountClosureSaga> logger,
        CancellationToken ct)
    {
        if (!TryMove(AccountClosureState.Completed, AuthEvents.AccountDeleted, clock, logger, AccountClosureState.HoldingTasks, AccountClosureState.Deciding))
            return;

        FinishedAt = StateChangedAt;
        await steps.CompleteAsync(Id, UserId, StartedAt, StateChangedAt, ct);
    }

    public ReleaseOwnerTasksIntegrationCommand? Handle(
        [SagaIdentityFrom(nameof(AccountClosureRejectedInfo.ClosureId))] AccountClosureRejectedInfo rejected,
        TimeProvider clock,
        ILogger<AccountClosureSaga> logger)
    {
        if (!TryMove(AccountClosureState.ReleasingTasks, AuthEvents.AccountClosureRejected, clock, logger, AccountClosureState.HoldingTasks, AccountClosureState.Deciding))
            return null;

        Reason = rejected.Reason;
        return new ReleaseOwnerTasksIntegrationCommand(Id, UserId);
    }

    public async Task Handle(
        [SagaIdentityFrom(nameof(OwnerTasksReleasedIntegrationEvent.ClosureId))] OwnerTasksReleasedIntegrationEvent released,
        IAccountClosureSteps steps,
        TimeProvider clock,
        ILogger<AccountClosureSaga> logger,
        CancellationToken ct)
    {
        if (!TryMove(AccountClosureState.Rejected, TaskEvents.OwnerTasksReleased, clock, logger, AccountClosureState.ReleasingTasks))
            return;

        FinishedAt = StateChangedAt;
        await steps.RejectAsync(Id, UserId, Reason!, StartedAt, StateChangedAt, ct);
    }

    public void Handle(
        [SagaIdentityFrom(nameof(AccountClosureOverdue.ClosureId))] AccountClosureOverdue overdue,
        IAccountClosureSteps steps,
        ILogger<AccountClosureSaga> logger)
    {
        if (State is AccountClosureState.Completed or AccountClosureState.Rejected || Overdue)
            return;

        Overdue = true;
        steps.MarkOverdue(State.ToString());
        LogOverdue(logger, Id, State, overdue.After);
    }

    public static void NotFound(OwnerTasksHeldIntegrationEvent held, ILogger<AccountClosureSaga> logger) =>
        LogUnknown(logger, held.ClosureId, TaskEvents.OwnerTasksHeld);

    public static void NotFound(OwnerTasksReleasedIntegrationEvent released, ILogger<AccountClosureSaga> logger) =>
        LogUnknown(logger, released.ClosureId, TaskEvents.OwnerTasksReleased);

    public static void NotFound(AccountDeletedInfo deleted, ILogger<AccountClosureSaga> logger) =>
        LogUnknown(logger, deleted.ClosureId, AuthEvents.AccountDeleted);

    public static void NotFound(AccountClosureRejectedInfo rejected, ILogger<AccountClosureSaga> logger) =>
        LogUnknown(logger, rejected.ClosureId, AuthEvents.AccountClosureRejected);

    public static void NotFound(AccountClosureOverdue overdue)
    {
    }

    private bool TryMove(AccountClosureState to, string reply, TimeProvider clock, ILogger logger, params AccountClosureState[] from)
    {
        AccountClosureTrace.Mark(Id);
        if (Array.IndexOf(from, State) < 0)
        {
            LogIgnored(logger, Id, reply, State);
            return false;
        }

        AccountClosureState was = State;
        State = to;
        StateChangedAt = clock.GetUtcNow();
        LogMoved(logger, Id, was, to, reply);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: the orchestrator moves from {From} to {To} after {Reply}")]
    private static partial void LogMoved(ILogger logger, Guid closureId, AccountClosureState from, AccountClosureState to, string reply);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account closure {ClosureId}: the orchestrator ignores {Reply} because it is {State}")]
    private static partial void LogIgnored(ILogger logger, Guid closureId, string reply, AccountClosureState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account closure {ClosureId}: no closure with this id, the orchestrator ignores {Reply}")]
    private static partial void LogUnknown(ILogger logger, Guid closureId, string reply);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account closure {ClosureId} is overdue: still {State} after {After}")]
    private static partial void LogOverdue(ILogger logger, Guid closureId, AccountClosureState state, TimeSpan after);
}
