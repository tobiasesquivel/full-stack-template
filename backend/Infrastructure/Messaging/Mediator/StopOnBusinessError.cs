using ErrorOr;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

namespace TemplateApp.Infrastructure.Messaging.Mediator;

internal sealed class StopOnBusinessError : SyncFrame
{
    private readonly Type _answer;
    private Variable? _result;

    public StopOnBusinessError(Type answer)
    {
        _answer = answer;
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        _result = chain.FindVariable(_answer);
        yield return _result;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.Write($"if ({_result!.Usage}.{nameof(IErrorOr.IsError)}) return;");
        Next?.GenerateCode(method, writer);
    }
}
