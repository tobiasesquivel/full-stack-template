namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

internal sealed class AccountClosureSteps : IAccountClosureSteps
{
    private readonly CompleteAccountClosure.CompleteAccountClosure _purge;
    private readonly CancelAccountClosure.CancelAccountClosure _restore;
    private readonly UserMetrics _metrics;

    public AccountClosureSteps(CompleteAccountClosure.CompleteAccountClosure purge, CancelAccountClosure.CancelAccountClosure restore, UserMetrics metrics)
    {
        _purge = purge;
        _restore = restore;
        _metrics = metrics;
    }

    public async Task CompleteAsync(Guid closureId, string userId, DateTimeOffset? startedAt, DateTimeOffset now, CancellationToken ct)
    {
        if (await _purge.PurgeAsync(closureId, userId, now, ct) is DateTimeOffset closingSince)
            _metrics.AccountClosureFinished(ClosureOutcome.Completed, now - (startedAt ?? closingSince));
    }

    public async Task RejectAsync(Guid closureId, string userId, string reason, DateTimeOffset? startedAt, DateTimeOffset now, CancellationToken ct)
    {
        if (await _restore.RestoreAsync(closureId, userId, reason, now, ct) is DateTimeOffset closingSince)
            _metrics.AccountClosureFinished(ClosureOutcome.Rejected, now - (startedAt ?? closingSince));
    }

    public void MarkOverdue(string state) => _metrics.AccountClosureOverdue(state);
}
