using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dune.Analyzers.Tests;

internal static class AnalyzerHost
{
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        IReadOnlyList<(string Path, string Source)> files,
        Func<ImmutableArray<DiagnosticAnalyzer>> analyzers)
    {
        var trees = files
            .Select(file => CSharpSyntaxTree.ParseText(
                SourceText.From(file.Source),
                new CSharpParseOptions(LanguageVersion.Latest),
                file.Path))
            .ToArray();

        var references = TestReferences.All();

        var compilation = CSharpCompilation.Create(
            "DuneBehavior",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers().ToImmutableArray(),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty));

        TestReferences.ThrowOnFixtureErrors(compilation);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    public static Func<ImmutableArray<DiagnosticAnalyzer>> All() =>
        () => ImmutableArray.Create<DiagnosticAnalyzer>(
            new DomainDependencyAnalyzer(),
            new EndpointLayerAnalyzer(),
            new InfrastructureBoundaryAnalyzer(),
            new CrossFeatureAnalyzer(),
            new NamingAnalyzer(),
            new DbSetShadowAnalyzer(),
            new OwnerPredicateAnalyzer(),
            new NoCommentsAnalyzer(),
            new OneTypePerFileAnalyzer(),
            new ApiRouteAnalyzer(),
            new VersionedApiAnalyzer(),
            new ConfigurationAccessAnalyzer());

    public static Func<ImmutableArray<DiagnosticAnalyzer>> Only<T>()
        where T : DiagnosticAnalyzer, new() =>
        () => ImmutableArray.Create<DiagnosticAnalyzer>(new T());
}
