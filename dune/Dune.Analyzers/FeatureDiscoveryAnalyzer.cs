using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FeatureDiscoveryAnalyzer : DiagnosticAnalyzer
{
    private const string FeatureInterface = "TemplateApp.Infrastructure.Hosting.IFeature";
    private const string TestsProject = "backend/Tests/";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.FeatureDiscovery);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (IsTestsProject(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
            {
                return;
            }

            var contract = start.Compilation.GetTypeByMetadataName(FeatureInterface);
            if (contract is not null)
            {
                start.RegisterSymbolAction(symbol => AnalyzeImplementation(symbol, contract), SymbolKind.NamedType);
            }

            start.RegisterCompilationEndAction(end => AnalyzeFeatures(end, contract));
        });
    }

    private static void AnalyzeImplementation(SymbolAnalysisContext context, INamedTypeSymbol contract)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default))
        {
            return;
        }

        foreach (var location in type.Locations.Where(location => location.IsInSource))
        {
            var path = DunePaths.Normalize(location.SourceTree?.FilePath);
            if (IsFeatureRoot(type, path))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.FeatureDiscovery,
                location,
                type.Name,
                "implementa IFeature y no es la clase <Feature>Feature de primer nivel, no genérica ni file, de Features/<Feature>/<Feature>Feature.cs: el host lo descubriría como una feature más o no lo podría crear"));
        }
    }

    private static bool IsTestsProject(AnalyzerConfigOptions options) =>
        options.TryGetValue(HostBoundaryAnalyzer.ProjectPathOption, out var projectPath)
        && DunePaths.Normalize(projectPath).TrimStart('/').StartsWith(TestsProject, StringComparison.OrdinalIgnoreCase);

    private static bool IsFeatureRoot(INamedTypeSymbol type, string path) =>
        type.ContainingType is null
        && type.Arity == 0
        && !type.IsFileLocal
        && DunePaths.TryGetFeature(path, out var feature)
        && DunePaths.FeatureSegments(path, feature).Length == 1
        && path.EndsWith("/" + feature + "Feature.cs", StringComparison.Ordinal)
        && type.Name.Equals(feature + "Feature", StringComparison.Ordinal);

    private static void AnalyzeFeatures(CompilationAnalysisContext context, INamedTypeSymbol? contract)
    {
        var features = new SortedDictionary<string, Location>(StringComparer.Ordinal);
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var path = DunePaths.Normalize(tree.FilePath);
            if (DunePaths.IsBackendFile(path)
                && !DunePaths.IsMigration(path)
                && DunePaths.TryGetFeature(path, out var feature)
                && !features.ContainsKey(feature))
            {
                features[feature] = Location.Create(tree, default);
            }
        }

        foreach (var pair in features)
        {
            var problem = Problem(context.Compilation, contract, pair.Key, out var type);
            if (problem is null)
            {
                continue;
            }

            var location = type?.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? pair.Value;
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.FeatureDiscovery, location, pair.Key + "Feature", problem));
        }
    }

    private static string? Problem(Compilation compilation, INamedTypeSymbol? contract, string feature, out INamedTypeSymbol? type)
    {
        type = compilation.GetTypeByMetadataName("TemplateApp.Features." + feature + "." + feature + "Feature");
        if (contract is null)
        {
            return "no se puede revisar: la compilación tiene la feature " + feature + " y no encuentra " + FeatureInterface;
        }

        if (type is null)
        {
            return "falta: la feature " + feature + " compila en este host y nadie la registra";
        }

        if (type.TypeKind != TypeKind.Class || type.IsStatic || type.IsAbstract)
        {
            return "es una clase estática, abstracta o no es una clase: el host no la puede crear";
        }

        if (!type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default))
        {
            return "no implementa IFeature: el host no la descubre";
        }

        if (!type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public))
        {
            return "no tiene un constructor público sin parámetros: el host no la puede crear";
        }

        return null;
    }
}
