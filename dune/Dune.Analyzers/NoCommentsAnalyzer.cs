using System;
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

        if (DuneCommentPolicy.IsExemptPath(path, RepositoryRoot(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions)))
        {
            return;
        }

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var trivia in DuneCommentPolicy.EnumerateComments(root))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.NoComments, trivia.GetLocation()));
        }
    }

    private static string? RepositoryRoot(AnalyzerConfigOptions options)
    {
        if (!options.TryGetValue(HostBoundaryAnalyzer.ProjectDirectoryOption, out var directory)
            || !options.TryGetValue(HostBoundaryAnalyzer.ProjectPathOption, out var projectPath)
            || string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        var project = DunePaths.Normalize(directory).TrimEnd('/');
        var relative = DunePaths.Normalize(projectPath).Trim('/');
        return project.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase)
            ? project.Substring(0, project.Length - relative.Length)
            : null;
    }

    private static bool IsDuneFile(string normalizedPath) =>
        DunePaths.ContainsOrdinal(normalizedPath, "dune/");
}
