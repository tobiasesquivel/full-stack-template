using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicSurfaceAnalyzer : DiagnosticAnalyzer
{
    private const string FeaturesNamespace = "TemplateApp.Features.";
    private const string ContractsSegment = ".Contracts";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.PublicInterior, DuneDiagnostics.ContractNotPublic);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.ContainingType is not null)
        {
            return;
        }

        var feature = string.Empty;
        var location = type.Locations.FirstOrDefault(candidate =>
            candidate.IsInSource
            && DunePaths.TryGetFeature(DunePaths.Normalize(candidate.SourceTree?.FilePath), out feature));
        if (location is null)
        {
            return;
        }

        var path = DunePaths.Normalize(location.SourceTree?.FilePath);
        if (DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path))
        {
            return;
        }

        var segments = DunePaths.FeatureSegments(path, feature);
        var isPublic = type.DeclaredAccessibility == Accessibility.Public;
        var isContract = segments.Length == 2
            && segments[0].Equals("Contracts", StringComparison.Ordinal)
            && IsContractsNamespace(DuneSymbols.NamespaceOf(type));
        if (isPublic && !isContract)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.PublicInterior, location, type.Name));
        }
        else if (!isPublic && isContract)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.ContractNotPublic, location, type.Name));
        }
    }

    private static bool IsContractsNamespace(string ns)
    {
        if (!ns.StartsWith(FeaturesNamespace, StringComparison.Ordinal)
            || !ns.EndsWith(ContractsSegment, StringComparison.Ordinal))
        {
            return false;
        }

        var feature = ns.Substring(FeaturesNamespace.Length, ns.Length - FeaturesNamespace.Length - ContractsSegment.Length);
        return feature.Length > 0 && feature.IndexOf('.') < 0;
    }
}
