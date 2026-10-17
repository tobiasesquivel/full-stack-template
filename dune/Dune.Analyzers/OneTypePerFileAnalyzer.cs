using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneTypePerFileAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.OneTypePerFile);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var path = DunePaths.Normalize(context.Tree.FilePath);
        if (!DunePaths.IsBackendFile(path)
            || DunePaths.IsMigration(path)
            || DunePaths.ContainsOrdinal(path, "/bin/")
            || DunePaths.ContainsOrdinal(path, "/obj/"))
        {
            return;
        }

        var types = context.Tree.GetRoot(context.CancellationToken)
            .DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<MemberDeclarationSyntax>()
            .Where(member => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Where(member => !member.Modifiers.Any(SyntaxKind.FileKeyword))
            .ToArray();

        if (types.Length < 2)
        {
            return;
        }

        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var owner = types.FirstOrDefault(type => IdentifierOf(type).Text == fileName) ?? types[0];
        foreach (var extra in types.Where(type => type != owner))
        {
            var identifier = IdentifierOf(extra);
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.OneTypePerFile,
                identifier.GetLocation(),
                identifier.Text,
                IdentifierOf(owner).Text));
        }
    }

    private static SyntaxToken IdentifierOf(MemberDeclarationSyntax member) =>
        member switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax @delegate => @delegate.Identifier,
            _ => default,
        };
}
