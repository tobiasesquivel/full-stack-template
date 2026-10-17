using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InfrastructureBoundaryAnalyzer : DiagnosticAnalyzer
{
    private const string FeaturesNamespace = "TemplateApp.Features.";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.InfrastructureModule);

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
        if (!IsGuardedInfrastructure(identifier))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        ReportIfFeatureInterior(context, identifier, symbol);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (!IsGuardedInfrastructure(access))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
        ReportIfFeatureInterior(context, access, symbol);
    }

    private static bool IsGuardedInfrastructure(SyntaxNode node)
    {
        var path = DunePaths.Normalize(node.SyntaxTree.FilePath);
        return DunePaths.IsBackendFile(path)
            && !DunePaths.IsMigration(path)
            && DunePaths.IsInfrastructureFile(path);
    }

    private static void ReportIfFeatureInterior(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        ISymbol? symbol)
    {
        var type = symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol method => method.ReducedFrom?.ContainingType ?? method.ContainingType,
            IPropertySymbol or IFieldSymbol or IEventSymbol => symbol.ContainingType,
            _ => null,
        };

        if (type is null || !IsFeatureInterior(DuneSymbols.NamespaceOf(type)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.InfrastructureModule,
            node.GetLocation(),
            symbol!.ToDisplayString()));
    }

    private static bool IsFeatureInterior(string ns)
    {
        if (!ns.StartsWith(FeaturesNamespace, System.StringComparison.Ordinal))
        {
            return false;
        }

        var rest = ns.Substring(FeaturesNamespace.Length);
        var dot = rest.IndexOf('.');
        var tail = dot < 0 ? string.Empty : rest.Substring(dot + 1);
        return !IsPublicSegment(tail, "Domain") && !IsPublicSegment(tail, "Contracts");
    }

    private static bool IsPublicSegment(string tail, string segment) =>
        tail.Equals(segment, System.StringComparison.Ordinal)
        || tail.StartsWith(segment + ".", System.StringComparison.Ordinal);
}
