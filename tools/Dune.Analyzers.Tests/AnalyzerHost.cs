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

// Behavioral host: runs the real analyzers over real compilations and returns
// the diagnostics they produce. Tests assert on diagnostic ids and locations,
// never on source text.
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

        // A fixture with missing references resolves to error types and every
        // symbol-gated rule silently passes. Fail loud instead of green.
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
            new OwnerPredicateAnalyzer());

    public static Func<ImmutableArray<DiagnosticAnalyzer>> Only<T>()
        where T : DiagnosticAnalyzer, new() =>
        () => ImmutableArray.Create<DiagnosticAnalyzer>(new T());
}

// In-memory compilations resolve fixture symbols against the test process's
// loaded assemblies. EF/Identity assemblies load on first use, so touching
// the fixture-relevant types before the AppDomain scan keeps DbContext,
// relational extensions, and Identity managers resolvable instead of
// degrading to error types.
internal static class TestReferences
{
    public static List<MetadataReference> All()
    {
        // TRUSTED_PLATFORM_ASSEMBLIES is the full shared-framework closure.
        // The AppDomain scan only sees already-loaded assemblies, so TPA is
        // the complete host. Touching fixture-relevant types forces their
        // loads before TPA is read.
        _ = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly;
        _ = typeof(Microsoft.EntityFrameworkCore.RelationalQueryableExtensions).Assembly;
        _ = typeof(Microsoft.AspNetCore.Identity.IdentityUser).Assembly;
        _ = typeof(Microsoft.AspNetCore.Identity.UserManager<>).Assembly;
        _ = typeof(System.Linq.Queryable).Assembly;

        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var paths = (tpa ?? string.Empty)
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var locations = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(
            Path.GetDirectoryName(typeof(TestReferences).Assembly.Location)!,
            "*.dll"))
        {
            locations.Add(file);
        }

        // TPA already carries the framework closure; the test output dir adds
        // the NuGet closure. Same filename in both (e.g. xunit) must resolve
        // once, preferring the test dir copy.
        return locations
            .GroupBy(location => Path.GetFileName(location), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(location =>
                Path.GetDirectoryName(location)!.StartsWith(
                    Path.GetDirectoryName(typeof(TestReferences).Assembly.Location)!,
                    StringComparison.OrdinalIgnoreCase) ? 1 : 0).First())
            .Select(location => MetadataReference.CreateFromFile(location))
            .Cast<MetadataReference>()
            .ToList();
    }

    public static void ThrowOnFixtureErrors(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && !diagnostic.Id.StartsWith("DUNE", StringComparison.Ordinal)
                && !diagnostic.Id.StartsWith("RS", StringComparison.Ordinal))
            .ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Fixture compilation has errors: "
                + string.Join("; ", errors.Select(error =>
                    error.Id + " " + error.GetMessage())));
        }
    }
}
