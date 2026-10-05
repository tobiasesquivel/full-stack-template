using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

// DUNE0006. Identity owns Users. A second DbSet hiding it with new breaks the
// cascade story and the single-profile model, so it is an error.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbSetShadowAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.DbSetShadow);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (!property.Modifiers.Any(SyntaxKind.NewKeyword))
        {
            return;
        }

        var path = DunePaths.Normalize(property.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path))
        {
            return;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken);
        if (symbol is not IPropertySymbol propertySymbol)
        {
            return;
        }

        if (propertySymbol.Type is not INamedTypeSymbol type
            || !DuneSymbols.IsDbSet(type))
        {
            return;
        }

        if (propertySymbol.ContainingType is not INamedTypeSymbol container
            || !DuneSymbols.DerivesFromDbContext(container, context.SemanticModel.Compilation))
        {
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.DbSetShadow,
            property.Identifier.GetLocation(),
            propertySymbol.Name));
    }
}
