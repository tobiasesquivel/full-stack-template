using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoCommentsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.NoComments);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var path = DunePaths.Normalize(context.Tree.FilePath);
        if (!DunePaths.IsBackendFile(path) && !IsDuneFile(path))
        {
            return;
        }

        if (DunePaths.ContainsOrdinal(path, "/bin/")
            || DunePaths.ContainsOrdinal(path, "/obj/"))
        {
            return;
        }

        if (DuneCommentPolicy.IsExemptPath(path))
        {
            return;
        }

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var trivia in DuneCommentPolicy.EnumerateComments(root))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.NoComments, trivia.GetLocation()));
        }
    }

    private static bool IsDuneFile(string normalizedPath) =>
        DunePaths.ContainsOrdinal(normalizedPath, "dune/");
}
