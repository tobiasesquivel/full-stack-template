using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ErrorCatalogAnalyzer : DiagnosticAnalyzer
{
    private const string ErrorType = "ErrorOr.Error";
    private const string CatalogSuffix = "Errors";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ErrorOutsideCatalog, DuneDiagnostics.ErrorCodePrefix);

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
        if (!IsErrorFactory(method))
        {
            return;
        }

        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path))
        {
            return;
        }

        if (!path.EndsWith(CatalogSuffix + ".cs", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.ErrorOutsideCatalog,
                invocation.Syntax.GetLocation(),
                method.Name));
            return;
        }

        if (DunePaths.TryGetFeature(path, out _) && !method.Name.Equals("Validation", StringComparison.Ordinal))
        {
            AnalyzePrefix(context, invocation);
        }
    }

    private static bool IsErrorFactory(IMethodSymbol method) =>
        method.IsStatic
        && method.ContainingType.ToDisplayString().Equals(ErrorType, StringComparison.Ordinal)
        && method.ReturnType.ToDisplayString().Equals(ErrorType, StringComparison.Ordinal);

    private static void AnalyzePrefix(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var catalog = context.ContainingSymbol.ContainingType;
        if (catalog is null
            || catalog.Name.Length <= CatalogSuffix.Length
            || !catalog.Name.EndsWith(CatalogSuffix, StringComparison.Ordinal))
        {
            return;
        }

        var argument = invocation.Arguments.FirstOrDefault(candidate => string.Equals(candidate.Parameter?.Name, "code", StringComparison.Ordinal));
        if (argument?.Value.ConstantValue is not { HasValue: true, Value: string code })
        {
            return;
        }

        var prefix = catalog.Name.Substring(0, catalog.Name.Length - CatalogSuffix.Length) + ".";
        if (code.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        var location = argument.ArgumentKind == ArgumentKind.Explicit
            ? argument.Syntax.GetLocation()
            : invocation.Syntax.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.ErrorCodePrefix,
            location,
            code,
            prefix,
            catalog.Name));
    }
}
