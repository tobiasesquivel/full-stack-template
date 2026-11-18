using System.Transactions;

namespace TemplateApp.Infrastructure.Persistence;

public static class CrossContextTransaction
{
    public static TransactionScope Begin() =>
        new(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);
}
