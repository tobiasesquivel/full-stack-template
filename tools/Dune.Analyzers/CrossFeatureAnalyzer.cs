using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

// DUNE0004. A feature never calls another feature's implementation. References
// to the other feature's Domain or Contracts are explicit and allowed.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossFeatureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.CrossFeature);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        var identifier = (IdentifierNameSyntax)context.Node;
        var path = DunePaths.Normalize(identifier.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path)
            || DunePaths.IsMigration(path)
            || !DunePaths.TryGetFeature(path, out var owner))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        ReportIfForeignImplementation(context, identifier, symbol, path, owner);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        var path = DunePaths.Normalize(access.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path)
            || DunePaths.IsMigration(path)
            || !DunePaths.TryGetFeature(path, out var owner))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
        ReportIfForeignImplementation(context, access, symbol, path, owner);
    }

    private static void ReportIfForeignImplementation(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        ISymbol? symbol,
        string path,
        string owner)
    {
        if (symbol is null || symbol is INamespaceSymbol || symbol is IMethodSymbol || symbol is IPropertySymbol || symbol is IFieldSymbol || symbol is IEventSymbol)
        {
            return;
        }

        var ns = ContainingFeatureNamespace(symbol);
        if (ns is null || !IsForeignFeatureNamespace(ns, owner) || IsAllowedSegment(ns, owner))
        {
            return;
        }

        if (IsAllowedByPath(symbol, owner, context.Compilation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.CrossFeature,
            node.GetLocation(),
            symbol.ToDisplayString()));
    }

    private static string? ContainingFeatureNamespace(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol named)
        {
            return DuneSymbols.NamespaceOf(named);
        }

        if (symbol.ContainingType is not null)
        {
            return DuneSymbols.NamespaceOf(symbol.ContainingType);
        }

        return symbol.ContainingNamespace?.ToDisplayString();
    }

    private static bool IsForeignFeatureNamespace(string ns, string owner) =>
        ns.StartsWith("TemplateApp.Features.", System.StringComparison.Ordinal)
        && !ns.Equals("TemplateApp.Features." + owner, System.StringComparison.Ordinal)
        && !ns.StartsWith("TemplateApp.Features." + owner + ".", System.StringComparison.Ordinal);

    private static bool IsAllowedSegment(string ns, string owner)
    {
        if (!IsForeignFeatureNamespace(ns, owner))
        {
            return true;
        }

        var rest = ns.Substring("TemplateApp.Features.".Length);
        var slash = rest.IndexOf('.');
        var foreign = slash < 0 ? rest : rest.Substring(0, slash);
        var tail = slash < 0 ? string.Empty : rest.Substring(slash + 1);
        return tail.Equals("Domain", System.StringComparison.Ordinal)
            || tail.StartsWith("Domain.", System.StringComparison.Ordinal)
            || tail.Equals("Contracts", System.StringComparison.Ordinal)
            || tail.StartsWith("Contracts.", System.StringComparison.Ordinal)
            || foreign.Length == 0;
    }

    private static bool IsAllowedByPath(ISymbol symbol, string owner, Compilation compilation)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var path = DunePaths.Normalize(reference.SyntaxTree.FilePath);
            if (DunePaths.ContainsOrdinal(path, "/Domain/")
                || DunePaths.ContainsOrdinal(path, "/Contracts/"))
            {
                return true;
            }
        }

        return false;
    }
}
