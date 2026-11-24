using System.Transactions;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

namespace TemplateApp.Infrastructure.Messaging.Mediator;

internal sealed class CompleteCrossContextTransaction : SyncFrame
{
    private Variable? _transaction;

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        _transaction = chain.FindVariable(typeof(TransactionScope));
        yield return _transaction;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.Write($"{_transaction!.Usage}.{nameof(TransactionScope.Complete)}();");
        Next?.GenerateCode(method, writer);
    }
}
