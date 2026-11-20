using System;
using System.Collections.Generic;
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
    private const string SagaBase = "Wolverine.Saga";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.PublicInterior, DuneDiagnostics.ContractNotPublic);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var sagaSurface = new Lazy<ImmutableHashSet<INamedTypeSymbol>>(() => SagaSurface(start.Compilation));
            start.RegisterSymbolAction(symbol => Analyze(symbol, sagaSurface), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, Lazy<ImmutableHashSet<INamedTypeSymbol>> sagaSurface)
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
        if (isPublic && !isContract && !sagaSurface.Value.Contains(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.PublicInterior, location, type.Name));
        }
        else if (!isPublic && isContract)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.ContractNotPublic, location, type.Name));
        }
    }

    private static ImmutableHashSet<INamedTypeSymbol> SagaSurface(Compilation compilation)
    {
        var surface = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var saga in SourceTypes(compilation.Assembly.GlobalNamespace).Where(IsSaga))
        {
            surface.Add(saga);
            foreach (var member in saga.GetMembers().Where(member => member.DeclaredAccessibility == Accessibility.Public))
            {
                foreach (var signatureType in SignatureTypes(member))
                {
                    AddWithArguments(surface, signatureType);
                }
            }
        }

        return surface.ToImmutable();
    }

    private static IEnumerable<ITypeSymbol> SignatureTypes(ISymbol member) => member switch
    {
        IMethodSymbol method => method.Parameters.Select(parameter => parameter.Type).Append(method.ReturnType),
        IPropertySymbol property => new[] { property.Type },
        IFieldSymbol field => new[] { field.Type },
        _ => Array.Empty<ITypeSymbol>(),
    };

    private static void AddWithArguments(ImmutableHashSet<INamedTypeSymbol>.Builder surface, ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        if (named.OriginalDefinition.Locations.Any(location => location.IsInSource))
        {
            surface.Add(named.OriginalDefinition);
        }

        foreach (var argument in named.TypeArguments)
        {
            AddWithArguments(surface, argument);
        }
    }

    private static bool IsSaga(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == SagaBase)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers().Where(type => type.Locations.Any(location => location.IsInSource)))
        {
            yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in SourceTypes(child))
            {
                yield return type;
            }
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
