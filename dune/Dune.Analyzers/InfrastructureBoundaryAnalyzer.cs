using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InfrastructureBoundaryAnalyzer : DiagnosticAnalyzer
{
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
        ReportIfModuleOrEndpoint(context, identifier, symbol);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (!IsGuardedInfrastructure(access))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
        ReportIfModuleOrEndpoint(context, access, symbol);
    }

    private static bool IsGuardedInfrastructure(SyntaxNode node)
    {
        var path = DunePaths.Normalize(node.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path)
            || DunePaths.IsMigration(path)
            || !DunePaths.IsInfrastructureFile(path))
        {
            return false;
        }

        return !node.Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .Any(method => method.Identifier.Text.Equals(
                "AddBackendInfrastructure",
                System.StringComparison.Ordinal));
    }

    private static void ReportIfModuleOrEndpoint(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        ISymbol? symbol)
    {
        if (symbol is null)
        {
            return;
        }

        if (symbol is INamedTypeSymbol named)
        {
            if (IsModuleOrEndpointType(named))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.InfrastructureModule,
                    node.GetLocation(),
                    named.ToDisplayString()));
            }

            return;
        }

        if (symbol is IMethodSymbol method
            && (IsModuleOrEndpointType(method.ContainingType)
                || IsMapEndpointsMethod(method)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.InfrastructureModule,
                node.GetLocation(),
                method.ToDisplayString()));
        }
    }

    private static bool IsModuleOrEndpointType(INamedTypeSymbol type) =>
        type.Name.EndsWith("Module", System.StringComparison.Ordinal)
        || type.Name.EndsWith("Endpoints", System.StringComparison.Ordinal);

    private static bool IsMapEndpointsMethod(IMethodSymbol method) =>
        method.Name.StartsWith("Map", System.StringComparison.Ordinal)
        && method.Name.EndsWith("Endpoints", System.StringComparison.Ordinal);
}
