using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

// DUNE0001. Domain stays pure: only the BCL, the shared Domain kernel, and
// the feature's own Domain. Every reference resolves to its bound symbol, so
// a using alias or a fully qualified name cannot dodge the check.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainDependencyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.DomainDependency);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Enum or TypeKind.Interface))
        {
            return;
        }

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var path = DunePaths.Normalize(reference.SyntaxTree.FilePath);
            if (!DunePaths.IsBackendFile(path)
                || DunePaths.IsMigration(path)
                || !DunePaths.IsDomainFile(path))
            {
                continue;
            }

            CheckBaseTypes(context, type);
            CheckMembers(context, type, path);
            return;
        }
    }

    private static void CheckBaseTypes(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        if (type.BaseType is not null
            && !type.BaseType.SpecialType.Equals(SpecialType.System_Object)
            && !IsAllowed(type.BaseType, context.Compilation, type))
        {
            Report(context, type, type.BaseType);
        }

        foreach (var contract in type.Interfaces)
        {
            if (!IsAllowed(contract, context.Compilation, type))
            {
                Report(context, type, contract);
            }
        }
    }

    private static void CheckMembers(SymbolAnalysisContext context, INamedTypeSymbol type, string path)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            ITypeSymbol? memberType = member switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                IMethodSymbol method => method.ReturnType,
                IEventSymbol @event => @event.Type,
                _ => null,
            };

            if (memberType is null)
            {
                continue;
            }

            if (!IsAllowed(memberType, context.Compilation, type))
            {
                Report(context, type, memberType);
            }

            if (member is IMethodSymbol methodSymbol)
            {
                foreach (var parameter in methodSymbol.Parameters)
                {
                    if (!IsAllowed(parameter.Type, context.Compilation, type))
                    {
                        Report(context, type, parameter.Type);
                    }
                }
            }
        }
    }

    private static bool IsAllowed(ITypeSymbol type, Compilation compilation, INamedTypeSymbol owner)
    {
        if (type is ITypeParameterSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsAllowed(array.ElementType, compilation, owner);
        }

        var unwrapped = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        if (unwrapped is not INamedTypeSymbol named)
        {
            return true;
        }

        if (named.OriginalDefinition.SpecialType is not SpecialType.None)
        {
            return true;
        }

        if (DuneSymbols.InNamespace(named.OriginalDefinition, "System"))
        {

            return true;
        }

        if (DuneSymbols.InNamespace(named.OriginalDefinition, "TemplateApp.Shared.Domain"))
        {
            return true;
        }

        var ownerFeature = OwnerFeature(owner);
        var usedNs = DuneSymbols.NamespaceOf(named.OriginalDefinition);
        if (!string.IsNullOrEmpty(ownerFeature)
            && usedNs.StartsWith("TemplateApp.Features." + ownerFeature + ".Domain", System.StringComparison.Ordinal))
        {
            return true;
        }

        return named.TypeKind is TypeKind.Enum
            || named.TypeKind is TypeKind.Delegate
            || named.TypeKind is TypeKind.TypeParameter;
    }

    private static string OwnerFeature(INamedTypeSymbol owner)
    {
        foreach (var reference in owner.DeclaringSyntaxReferences)
        {
            var path = DunePaths.Normalize(reference.SyntaxTree.FilePath);
            if (DunePaths.TryGetFeature(path, out var feature))
            {
                return feature;
            }
        }

        var ns = DuneSymbols.NamespaceOf(owner);
        const string prefix = "TemplateApp.Features.";
        if (ns.StartsWith(prefix, System.StringComparison.Ordinal))
        {
            var rest = ns.Substring(prefix.Length);
            var dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest.Substring(0, dot);
        }

        return string.Empty;
    }

    private static void Report(SymbolAnalysisContext context, INamedTypeSymbol owner, ITypeSymbol used)
    {
        var location = owner.Locations.Length > 0 ? owner.Locations[0] : Location.None;
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.DomainDependency,
            location,
            used.ToDisplayString()));
    }
}
