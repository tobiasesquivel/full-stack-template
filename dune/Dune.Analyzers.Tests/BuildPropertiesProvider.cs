using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers.Tests;

internal sealed class BuildPropertiesProvider : AnalyzerConfigOptionsProvider
{
    private readonly Options _global;

    public BuildPropertiesProvider(ImmutableDictionary<string, string> properties) =>
        _global = new Options(properties);

    public override AnalyzerConfigOptions GlobalOptions => _global;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

    private sealed class Options : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new(ImmutableDictionary<string, string>.Empty);

        private readonly ImmutableDictionary<string, string> _values;

        public Options(ImmutableDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
            _values.TryGetValue(key, out value);
    }
}
