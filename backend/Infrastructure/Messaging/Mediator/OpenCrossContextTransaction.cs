using System.Transactions;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using TemplateApp.Infrastructure.Persistence.Transactions;

namespace TemplateApp.Infrastructure.Messaging.Mediator;

internal sealed class OpenCrossContextTransaction : SyncFrame
{
    private readonly Variable _transaction;

    public OpenCrossContextTransaction()
    {
        _transaction = Create<TransactionScope>("crossContextTransaction");
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.Write($"using var {_transaction.Usage} = global::{typeof(CrossContextTransaction).FullName}.{nameof(CrossContextTransaction.Begin)}();");
        Next?.GenerateCode(method, writer);
    }
}
