using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IdempotentPostAnalyzer : DiagnosticAnalyzer
{
    private const string ExcludedFeature = "Auth";
    private const string Opt = "WithIdempotency";
    private const string OptNamespace = "TemplateApp.Infrastructure.Http.Idempotency";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.IdempotentPost);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!method.Name.Equals("MapPost", StringComparison.Ordinal)
            || !DuneSymbols.InNamespace(method.ContainingType, "Microsoft.AspNetCore"))
        {
            return;
        }

        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!path.EndsWith("Endpoint.cs", StringComparison.Ordinal)
            || DunePaths.IsTestsFile(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || feature.Equals(ExcludedFeature, StringComparison.Ordinal)
            || ChainsOptIn(invocation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.IdempotentPost,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol.ContainingType?.Name ?? context.ContainingSymbol.Name));
    }

    private static bool ChainsOptIn(IOperation operation)
    {
        while (true)
        {
            var parent = operation.Parent;
            if (parent is IConversionOperation { IsImplicit: true })
            {
                operation = parent;
                continue;
            }

            var next = parent switch
            {
                IArgumentOperation { Parameter.Ordinal: 0, Parent: IInvocationOperation call } when call.TargetMethod.IsExtensionMethod => call,
                IInvocationOperation call when call.Instance == operation => call,
                _ => null,
            };

            if (next is null)
            {
                return false;
            }

            if (IsOptIn(next.TargetMethod))
            {
                return true;
            }

            operation = next;
        }
    }

    private static bool IsOptIn(IMethodSymbol method) =>
        method.Name.Equals(Opt, StringComparison.Ordinal)
        && method.IsGenericMethod
        && DuneSymbols.NamespaceOf(method.ContainingType).Equals(OptNamespace, StringComparison.Ordinal);
}
