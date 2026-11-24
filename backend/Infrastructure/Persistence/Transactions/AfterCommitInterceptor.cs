using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TemplateApp.Infrastructure.Persistence.Transactions;

internal sealed class AfterCommitInterceptor : DbTransactionInterceptor
{
    private readonly AfterCommit _afterCommit;

    public AfterCommitInterceptor(AfterCommit afterCommit)
    {
        _afterCommit = afterCommit;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => _afterCommit.Run();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _afterCommit.Run();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _afterCommit.Discard();

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _afterCommit.Discard();
        return Task.CompletedTask;
    }
}
