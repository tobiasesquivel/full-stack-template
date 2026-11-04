using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContractsAnalyzer : DiagnosticAnalyzer
{
    private const string FeaturesNamespace = "TemplateApp.Features.";
    private const string ContractsSegment = ".Contracts";
    private const string IdentityException = "TemplateApp.Features.Auth.Contracts.AppIdentityUser";

    private static readonly string[] DataSuffixes = { "Details", "Summary", "Info" };
    private static readonly string[] ConstantSuffixes = { "Permissions", "EndpointNames" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ContractKind, DuneDiagnostics.ContractDoorSignature, DuneDiagnostics.ContractUnused);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var declared = new ConcurrentDictionary<ISymbol, Location>(SymbolEqualityComparer.Default);
            var used = new ConcurrentDictionary<ISymbol, byte>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(symbol => AnalyzeContract(symbol, declared), SymbolKind.NamedType);
            start.RegisterSyntaxNodeAction(node => RecordUse(node, used), SyntaxKind.IdentifierName);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (var contract in declared)
                {
                    if (!used.ContainsKey(contract.Key))
                    {
                        end.ReportDiagnostic(Diagnostic.Create(
                            DuneDiagnostics.ContractUnused,
                            contract.Value,
                            DisplayName(contract.Key)));
                    }
                }
            });
        });
    }

    private static void AnalyzeContract(SymbolAnalysisContext context, ConcurrentDictionary<ISymbol, Location> declared)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (type.ContainingType is not null || location is null)
        {
            return;
        }

        var path = DunePaths.Normalize(location.SourceTree?.FilePath);
        if (DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path) || !TryGetContractsFeature(type, out var feature))
        {
            return;
        }

        if (type.ToDisplayString().Equals(IdentityException, StringComparison.Ordinal))
        {
            return;
        }

        var door = "I" + feature + "Module";
        if (!IsAdmitted(type, door))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.ContractKind, location, type.Name, door));
            return;
        }

        declared.TryAdd(type, location);
        if (type.TypeKind != TypeKind.Interface)
        {
            return;
        }

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            var methodLocation = method.Locations.FirstOrDefault(candidate => candidate.IsInSource) ?? location;
            declared.TryAdd(method, methodLocation);

            var exposed = method.Parameters.Select(parameter => parameter.Type)
                .Prepend(method.ReturnType)
                .Select(FindForbidden)
                .FirstOrDefault(found => found is not null);
            if (exposed is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.ContractDoorSignature,
                    methodLocation,
                    type.Name,
                    exposed.Name));
            }
        }
    }

    private static bool IsAdmitted(INamedTypeSymbol type, string door)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            return type.Name.Equals(door, StringComparison.Ordinal);
        }

        if (type.IsRecord)
        {
            return EndsWithAny(type.Name, DataSuffixes) || type.Name.EndsWith("Response", StringComparison.Ordinal);
        }

        if (!type.IsStatic)
        {
            return false;
        }

        var members = type.GetMembers().Where(member => !member.IsImplicitlyDeclared).ToArray();
        if (EndsWithAny(type.Name, ConstantSuffixes))
        {
            return members.All(member => member is IFieldSymbol { IsConst: true });
        }

        return type.Name.EndsWith("Extensions", StringComparison.Ordinal)
            && members.All(member => member is IMethodSymbol { IsExtensionMethod: true });
    }

    private static INamedTypeSymbol? FindForbidden(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindForbidden(array.ElementType);
            case INamedTypeSymbol named:
                if (!IsAllowedInDoor(named))
                {
                    return named;
                }

                return named.TypeArguments.Select(FindForbidden).FirstOrDefault(found => found is not null);
            default:
                return null;
        }
    }

    private static bool IsAllowedInDoor(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        if (DuneSymbols.InNamespace(definition, "System")
            || DuneSymbols.InNamespace(definition, "ErrorOr")
            || DuneSymbols.InNamespace(definition, "TemplateApp.Shared"))
        {
            return true;
        }

        return TryGetContractsFeature(definition, out _) && definition.IsRecord && EndsWithAny(definition.Name, DataSuffixes);
    }

    private static void RecordUse(SyntaxNodeAnalysisContext context, ConcurrentDictionary<ISymbol, byte> used)
    {
        var path = DunePaths.Normalize(context.Node.SyntaxTree.FilePath);
        if (DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path) || !DunePaths.TryGetFeature(path, out var user))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        switch (symbol)
        {
            case INamedTypeSymbol type:
                MarkUsed(type.OriginalDefinition, type.OriginalDefinition, user, used);
                break;
            case IMethodSymbol method:
                var definition = (method.ReducedFrom ?? method).OriginalDefinition;
                MarkUsed(definition, definition.ContainingType, user, used);
                MarkUsed(definition.ContainingType, definition.ContainingType, user, used);
                break;
            case IFieldSymbol or IPropertySymbol or IEventSymbol:
                MarkUsed(symbol.ContainingType.OriginalDefinition, symbol.ContainingType.OriginalDefinition, user, used);
                break;
        }
    }

    private static void MarkUsed(ISymbol symbol, INamedTypeSymbol owner, string user, ConcurrentDictionary<ISymbol, byte> used)
    {
        if (TryGetContractsFeature(owner, out var feature) && !feature.Equals(user, StringComparison.Ordinal))
        {
            used.TryAdd(symbol, 0);
        }
    }

    private static bool TryGetContractsFeature(INamedTypeSymbol type, out string feature)
    {
        feature = string.Empty;
        var ns = DuneSymbols.NamespaceOf(type);
        if (!ns.StartsWith(FeaturesNamespace, StringComparison.Ordinal)
            || !ns.EndsWith(ContractsSegment, StringComparison.Ordinal))
        {
            return false;
        }

        feature = ns.Substring(FeaturesNamespace.Length, ns.Length - FeaturesNamespace.Length - ContractsSegment.Length);
        return feature.Length > 0 && feature.IndexOf('.') < 0;
    }

    private static bool EndsWithAny(string name, IEnumerable<string> suffixes) =>
        suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length);

    private static string DisplayName(ISymbol symbol) =>
        symbol is IMethodSymbol method ? method.ContainingType.Name + "." + method.Name : symbol.Name;
}
