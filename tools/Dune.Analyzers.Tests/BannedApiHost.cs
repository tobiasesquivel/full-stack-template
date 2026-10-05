using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dune.Analyzers.Tests;

// Runs the real BannedApiAnalyzers package against an in-memory compilation.
// The banned entries travel as AdditionalText so the behavior under test is
// symbol resolution, not string matching.
internal static class BannedApiHost
{
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string bannedSymbols,
        IReadOnlyList<(string Path, string Source)> files)
    {
        var trees = files
            .Select(file => CSharpSyntaxTree.ParseText(
                SourceText.From(file.Source),
                new CSharpParseOptions(LanguageVersion.Latest),
                file.Path))
            .ToArray();

        var references = TestReferences.All();

        var compilation = CSharpCompilation.Create(
            "DuneBannedBehavior",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Microsoft.CodeAnalysis.CSharp.BannedApiAnalyzers.CSharpSymbolIsBannedAnalyzer();
        var additionalText = new InMemoryAdditionalText("BannedSymbols.txt", bannedSymbols);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(additionalText)));

        TestReferences.ThrowOnFixtureErrors(compilation);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _path;
        private readonly string _text;

        public InMemoryAdditionalText(string path, string text)
        {
            _path = path;
            _text = text;
        }

        public override string Path => _path;

        public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default) =>
            SourceText.From(_text);
    }
}
