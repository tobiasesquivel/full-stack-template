using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

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
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var named = (INamedTypeSymbol)context.Symbol;
        if (named.ContainingType is not null)
        {
            return;
        }

        foreach (var location in named.Locations)
        {
            var path = DunePaths.Normalize(location.SourceTree?.FilePath);
            if (!location.IsInSource
                || !DunePaths.IsBackendFile(path)
                || DunePaths.IsMigration(path)
                || DunePaths.IsTestsFile(path)
                || IsHostProgram(path))
            {
                continue;
            }

            var expected = DunePaths.ExpectedNamespace(path);
            var actual = DuneSymbols.NamespaceOf(named);
            if (expected is not null && !actual.Equals(expected, System.StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.NamespacePath,
                    location,
                    actual,
                    expected));
            }

            CheckSuffix(context, location, named, path);
        }
    }

    private static void CheckSuffix(
        SymbolAnalysisContext context,
        Location declaration,
        INamedTypeSymbol named,
        string path)
    {
        var name = named.Name;
        if (DunePaths.TryGetFeature(path, out var feature))
        {
            if (IsFeatureDoor(named, path, feature))
            {
                return;
            }

            if (name.EndsWith("Module", System.StringComparison.Ordinal)
                || name.EndsWith("Endpoints", System.StringComparison.Ordinal))
            {
                Report(context, declaration, name, "una clase por slice en su carpeta, sin clase compartida por todas las slices");
                return;
            }

            if (name.EndsWith("Endpoint", System.StringComparison.Ordinal))
            {
                if (!DunePaths.TryGetSlice(path, out var slice) || !name.Equals(slice + "Endpoint", System.StringComparison.Ordinal))
                {
                    Report(context, declaration, name, "<Slice>Endpoint dentro de la carpeta de su slice (Features/<Feature>/<Slice>/)");
                }

                return;
            }

            if (name.EndsWith("Feature", System.StringComparison.Ordinal))
            {
                if (DunePaths.FeatureSegments(path, feature).Length != 1 || !name.Equals(feature + "Feature", System.StringComparison.Ordinal))
                {
                    Report(context, declaration, name, feature + "Feature en la raíz de la feature");
                }

                return;
            }
        }

        if (name.EndsWith("Tests", System.StringComparison.Ordinal)
            || (name.EndsWith("Test", System.StringComparison.Ordinal) && !name.Equals("Test", System.StringComparison.Ordinal)))
        {
            Report(context, declaration, name, "<Slice>Tests.cs junto a su slice, o backend/Tests/");
            return;
        }

        if (name.EndsWith("Request", System.StringComparison.Ordinal)
            || name.EndsWith("Response", System.StringComparison.Ordinal)
            || name.EndsWith("Validator", System.StringComparison.Ordinal))
        {
            if (DunePaths.IsDomainFile(path))
            {
                Report(context, declaration, name, "fuera de Domain (en la slice o en Contracts)");
            }
        }
    }

    private static bool IsFeatureDoor(INamedTypeSymbol named, string path, string feature)
    {
        var door = "I" + feature + "Module";
        var segments = DunePaths.FeatureSegments(path, feature);
        if (named.TypeKind == TypeKind.Interface)
        {
            return named.Name.Equals(door, System.StringComparison.Ordinal)
                && segments.Length == 2
                && segments[0].Equals("Contracts", System.StringComparison.Ordinal);
        }

        return named.TypeKind == TypeKind.Class
            && named.Name.Equals(feature + "Module", System.StringComparison.Ordinal)
            && segments.Length == 1
            && named.Interfaces.Any(contract =>
                contract.Name.Equals(door, System.StringComparison.Ordinal)
                && DuneSymbols.NamespaceOf(contract).Equals(
                    "TemplateApp.Features." + feature + ".Contracts",
                    System.StringComparison.Ordinal));
    }

    private static void Report(
        SymbolAnalysisContext context,
        Location declaration,
        string name,
        string expected) =>
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.SuffixPlacement,
            declaration,
            name,
            expected));

    private static bool IsHostProgram(string path)
    {
        if (path.EndsWith("backend/Program.cs", System.StringComparison.Ordinal))
        {
            return true;
        }

        var marker = path.IndexOf("backend/Hosts/", System.StringComparison.Ordinal);
        if (marker < 0)
        {
            return false;
        }

        var segments = path.Substring(marker + "backend/Hosts/".Length).Split('/');
        return segments.Length == 2 && segments[1].Equals("Program.cs", System.StringComparison.Ordinal);
    }
}
