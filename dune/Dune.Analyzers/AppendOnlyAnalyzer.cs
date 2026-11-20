using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AppendOnlyAnalyzer : DiagnosticAnalyzer
{
    private const string MarkerName = "IAppendOnly";
    private const string MarkerNamespace = "TemplateApp.Shared.Domain";

    private static readonly ImmutableHashSet<string> Rewrites = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Update",
        "UpdateRange",
        "Remove",
        "RemoveRange",
        "ExecuteUpdate",
        "ExecuteUpdateAsync",
        "ExecuteDelete",
        "ExecuteDeleteAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.AppendOnlyWrite, DuneDiagnostics.AppendOnlySetter);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!Rewrites.Contains(method.Name)
            || !DuneSymbols.InNamespace(method.ContainingType, "Microsoft.EntityFrameworkCore")
            || !InProduct(invocation.Syntax.SyntaxTree.FilePath))
        {
            return;
        }

        var entity = Touched(invocation);
        if (entity is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.AppendOnlyWrite,
            invocation.Syntax.GetLocation(),
            method.Name,
            entity.Name));
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.SetMethod is null
            || property.SetMethod.IsInitOnly
            || property.IsStatic
            || !(property.ContainingType is { } owner)
            || !IsAppendOnly(owner))
        {
            return;
        }

        foreach (var location in property.Locations.Where(location => location.IsInSource && InProduct(location.SourceTree!.FilePath)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.AppendOnlySetter,
                location,
                property.Name,
                owner.Name));
        }
    }

    private static INamedTypeSymbol? Touched(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        foreach (var argument in method.TypeArguments.OfType<INamedTypeSymbol>())
        {
            if (IsAppendOnly(argument))
            {
                return argument;
            }
        }

        if (method.ContainingType is { } owner && DuneSymbols.IsDbSet(owner) && owner.TypeArguments[0] is INamedTypeSymbol set && IsAppendOnly(set))
        {
            return set;
        }

        foreach (var argument in invocation.Arguments)
        {
            var value = argument.Value is IConversionOperation conversion ? conversion.Operand : argument.Value;
            if (value.Type is INamedTypeSymbol named && IsAppendOnly(named))
            {
                return named;
            }

            if (value is IArrayCreationOperation { Initializer: { } initializer })
            {
                foreach (var element in initializer.ElementValues)
                {
                    var item = element is IConversionOperation inner ? inner.Operand : element;
                    if (item.Type is INamedTypeSymbol itemType && IsAppendOnly(itemType))
                    {
                        return itemType;
                    }
                }
            }

            if (value.Type is IArrayTypeSymbol { ElementType: INamedTypeSymbol arrayElement } && IsAppendOnly(arrayElement))
            {
                return arrayElement;
            }

            if (value.Type is INamedTypeSymbol sequence)
            {
                var elementType = sequence.AllInterfaces
                    .Concat(sequence.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ? new[] { sequence } : Array.Empty<INamedTypeSymbol>())
                    .Where(type => type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                    .Select(type => type.TypeArguments[0])
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault(IsAppendOnly);
                if (elementType is not null)
                {
                    return elementType;
                }
            }
        }

        return null;
    }

    private static bool InProduct(string filePath)
    {
        var path = DunePaths.Normalize(filePath);
        return DunePaths.IsBackendFile(path) && !DunePaths.IsMigration(path) && !DunePaths.IsTestsFile(path);
    }

    private static bool IsAppendOnly(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(candidate =>
            candidate.Name.Equals(MarkerName, StringComparison.Ordinal)
            && DuneSymbols.NamespaceOf(candidate).Equals(MarkerNamespace, StringComparison.Ordinal));
}
