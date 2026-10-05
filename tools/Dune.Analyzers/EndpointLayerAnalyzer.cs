using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

// DUNE0002. Endpoints adapt HTTP and call their module. Direct touches of EF,
// Persistence, or Identity managers are a layer violation, whatever spelling
// the reference uses.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointLayerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.EndpointPersistence);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMember, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMember, SyntaxKind.AliasQualifiedName);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
    }

    private static void AnalyzeMember(SyntaxNodeAnalysisContext context)
    {
        var path = DunePaths.Normalize(context.Node.SyntaxTree.FilePath);
        if (!IsEndpoint(path))
        {
            return;
        }

        // AliasQualifiedName (EF::DbSet<T>, Identity::UserManager<TUser>) is a
        // distinct syntax kind from SimpleMemberAccessExpression; resolving the
        // whole node surfaces the bound symbol through the alias.
        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null)
        {
            return;
        }

        if (DuneSymbols.TouchesPersistenceOrEf(symbol, context.SemanticModel.Compilation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.EndpointPersistence,
                context.Node.GetLocation(),
                symbol.ToDisplayString()));
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        var path = DunePaths.Normalize(creation.SyntaxTree.FilePath);
        if (!IsEndpoint(path))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(creation.Type, context.CancellationToken).Symbol;
        if (symbol is INamedTypeSymbol named
            && DuneSymbols.TouchesPersistenceOrEf(named, context.SemanticModel.Compilation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.EndpointPersistence,
                creation.Type.GetLocation(),
                named.ToDisplayString()));
        }
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.Parent is not VariableDeclarationSyntax
            && context.Node.Parent is not ParameterSyntax
            && context.Node.Parent is not FieldDeclarationSyntax
            && context.Node.Parent is not PropertyDeclarationSyntax)
        {
            return;
        }

        var identifier = (IdentifierNameSyntax)context.Node;
        var path = DunePaths.Normalize(identifier.SyntaxTree.FilePath);
        if (!IsEndpoint(path))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        if (symbol is INamedTypeSymbol named
            && DuneSymbols.TouchesPersistenceOrEf(named, context.SemanticModel.Compilation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.EndpointPersistence,
                identifier.GetLocation(),
                named.ToDisplayString()));
        }
    }

    private static bool IsEndpoint(string path) =>
        DunePaths.IsBackendFile(path)
        && !DunePaths.IsMigration(path)
        && !DunePaths.IsTestsFile(path)
        && DunePaths.IsEndpointFile(path, System.IO.Path.GetFileName(path));
}
