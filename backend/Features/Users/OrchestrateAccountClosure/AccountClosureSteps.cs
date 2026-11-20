using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

internal sealed class AccountClosureSteps : IAccountClosureSteps
{
    private readonly CompleteAccountClosure.CompleteAccountClosure _purge;
    private readonly CancelAccountClosure.CancelAccountClosure _restore;
    private readonly AccountClosureHistory _history;
    private readonly UserMetrics _metrics;
    private readonly AfterCommit _afterCommit;

    public AccountClosureSteps(
        CompleteAccountClosure.CompleteAccountClosure purge,
        CancelAccountClosure.CancelAccountClosure restore,
        AccountClosureHistory history,
        UserMetrics metrics,
        AfterCommit afterCommit)
    {
        _purge = purge;
        _restore = restore;
        _history = history;
        _metrics = metrics;
        _afterCommit = afterCommit;
    }

    public async Task CompleteAsync(Guid closureId, string userId, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken ct)
    {
        if (!await _purge.PurgeAsync(closureId, userId, now, ct))
            return;

        await _history.RecordAsync(closureId, userId, AccountClosureFacts.Completed, AccountClosureFacts.SystemActor, null, now, ct);
        _afterCommit.Add(() => _metrics.AccountClosureFinished(ClosureOutcome.Completed, now - startedAt));
    }

    public async Task RejectAsync(Guid closureId, string userId, string reason, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken ct)
    {
        if (!await _restore.RestoreAsync(closureId, userId, reason, now, ct))
            return;

        await _history.RecordAsync(closureId, userId, AccountClosureFacts.Rejected, AccountClosureFacts.SystemActor, reason, now, ct);
        _afterCommit.Add(() => _metrics.AccountClosureFinished(ClosureOutcome.Rejected, now - startedAt));
    }

    public void MarkOverdue(string state) => _afterCommit.Add(() => _metrics.AccountClosureOverdue(state));
}
