using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuditBypassAnalyzer : DiagnosticAnalyzer
{
    private const string MarkerName = "IAuditable";
    private const string MarkerNamespace = "TemplateApp.Shared.Domain";

    private static readonly ImmutableHashSet<string> BulkWrites = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ExecuteUpdate",
        "ExecuteUpdateAsync",
        "ExecuteDelete",
        "ExecuteDeleteAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.AuditBypass);

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
        if (!BulkWrites.Contains(method.Name)
            || !DuneSymbols.InNamespace(method.ContainingType, "Microsoft.EntityFrameworkCore")
            || method.TypeArguments.Length != 1
            || !(method.TypeArguments[0] is INamedTypeSymbol entity)
            || !IsAuditable(entity))
        {
            return;
        }

        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.AuditBypass,
            invocation.Syntax.GetLocation(),
            method.Name,
            entity.Name));
    }

    private static bool IsAuditable(INamedTypeSymbol entity) =>
        entity.AllInterfaces.Any(type =>
            type.Name.Equals(MarkerName, StringComparison.Ordinal)
            && DuneSymbols.NamespaceOf(type).Equals(MarkerNamespace, StringComparison.Ordinal));
}
