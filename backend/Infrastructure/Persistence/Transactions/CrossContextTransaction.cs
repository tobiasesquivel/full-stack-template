using System.Transactions;

namespace TemplateApp.Infrastructure.Persistence.Transactions;

public static class CrossContextTransaction
{
    public static TransactionScope Begin() =>
        new(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);
}
