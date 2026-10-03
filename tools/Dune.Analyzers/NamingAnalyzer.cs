using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

// DUNE0005 and DUNE0008. Namespaces track the file location so a reader can
// find the owning module, and each meaningful suffix lives in exactly one
// place. Migrations and the model snapshot keep their historical namespaces
// and stay exempt.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DuneDiagnostics.NamespacePath,
            DuneDiagnostics.SuffixPlacement);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.StructDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.RecordDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.InterfaceDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeDeclaration, SyntaxKind.EnumDeclaration);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (BaseTypeDeclarationSyntax)context.Node;
        var path = DunePaths.Normalize(declaration.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path))
        {
            return;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (symbol is not INamedTypeSymbol named || named.ContainingType is not null)
        {
            return;
        }

        // backend/Program.cs uses top-level statements. The compiler synthesizes
        // a Program type in the global namespace for that file shape. Naming the
        // file Program and keeping top-level statements is deliberate, so the
        // synthesized type stays out of DUNE0005 regardless of its display name.
        if (DunePaths.Normalize(declaration.SyntaxTree.FilePath).EndsWith("backend/Program.cs", System.StringComparison.Ordinal))
        {
            return;
        }

        var expected = DunePaths.ExpectedNamespace(path);
        var actual = DuneSymbols.NamespaceOf(named);
        if (expected is not null && !actual.Equals(expected, System.StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.NamespacePath,
                declaration.Identifier.GetLocation(),
                actual,
                expected));
        }

        CheckSuffix(context, declaration, named, path);
    }

    private static void CheckSuffix(
        SyntaxNodeAnalysisContext context,
        BaseTypeDeclarationSyntax declaration,
        INamedTypeSymbol named,
        string path)
    {
        var name = named.Name;
        if (name.EndsWith("Module", System.StringComparison.Ordinal)
            || name.EndsWith("Endpoints", System.StringComparison.Ordinal))
        {
            if (!IsFeatureRoot(path))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.SuffixPlacement,
                    declaration.Identifier.GetLocation(),
                    name,
                    "la raíz de la feature (TemplateApp.Features.<Feature>)"));
            }

            return;
        }

        if (name.EndsWith("Tests", System.StringComparison.Ordinal)
            || (name.EndsWith("Test", System.StringComparison.Ordinal) && !name.Equals("Test", System.StringComparison.Ordinal)))
        {
            if (!DunePaths.IsTestsFile(path) && !DunePaths.ContainsOrdinal(path, "/Tests"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.SuffixPlacement,
                    declaration.Identifier.GetLocation(),
                    name,
                    "un directorio Tests/ de la feature"));
            }

            return;
        }

        if (name.EndsWith("Request", System.StringComparison.Ordinal)
            || name.EndsWith("Response", System.StringComparison.Ordinal)
            || name.EndsWith("Validator", System.StringComparison.Ordinal))
        {
            if (DunePaths.IsDomainFile(path))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.SuffixPlacement,
                    declaration.Identifier.GetLocation(),
                    name,
                    "fuera de Domain (contratos o validación junto a la feature)"));
            }
        }
    }

    private static bool IsFeatureRoot(string path)
    {
        if (!DunePaths.TryGetFeature(path, out var feature))
        {
            return false;
        }

        var marker = "backend/Features/" + feature + "/";
        var index = path.IndexOf(marker, System.StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var rest = path.Substring(index + marker.Length);
        return rest.IndexOf('/') < 0;
    }
}
