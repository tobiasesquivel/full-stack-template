using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OwnerPredicateAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.OwnerPredicate);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        if (context.Operation is not IInvocationOperation invocation)
        {
            return;
        }

        if (!IsQueryOperator(invocation.TargetMethod))
        {
            return;
        }

        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name.Equals("predicate", System.StringComparison.Ordinal) != true
                && argument.Parameter?.Ordinal != 1)
            {
                continue;
            }

            if (SeesIdAndOwner(argument.Value))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.OwnerPredicate,
                    invocation.Syntax.GetLocation()));
            }
        }
    }

    private static bool IsQueryOperator(IMethodSymbol method)
    {
        if (method.Name is not ("Where" or "FirstOrDefault" or "First" or "SingleOrDefault" or "Single" or "Find"))
        {
            return false;
        }

        var ns = DuneSymbols.NamespaceOf(method.ContainingType);
        return ns.Equals("System.Linq", System.StringComparison.Ordinal)
            || ns.Equals("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal)
            || DuneSymbols.InNamespace(method.ContainingType, "System.Linq");
    }

    private static bool SeesIdAndOwner(IOperation value)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        CollectMemberNames(value, seen);
        return seen.Contains("Id") && (seen.Contains("OwnerId") || seen.Contains("AuthId"));
    }

    private static void CollectMemberNames(IOperation operation, HashSet<string> seen)
    {
        if (operation is IMemberReferenceOperation member)
        {
            seen.Add(member.Member.Name);
        }

        if (operation is IParameterReferenceOperation)
        {
            return;
        }

        foreach (var child in operation.Children)
        {
            CollectMemberNames(child, seen);
        }
    }
}
