using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossFeatureAnalyzer : DiagnosticAnalyzer
{
    private const string FeaturesNamespace = "TemplateApp.Features.";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.CrossFeature);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IdentifierName, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        var path = DunePaths.Normalize(node.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path)
            || DunePaths.IsMigration(path)
            || !DunePaths.TryGetFeature(path, out var owner))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
        var foreign = symbol is ITypeSymbol named
            ? FindForeignInterior(named, owner)
            : FindForeignInterior(context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type, owner);

        if (foreign is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.CrossFeature,
            node.GetLocation(),
            foreign.ToDisplayString()));
    }

    private static ITypeSymbol? FindForeignInterior(ITypeSymbol? type, string owner)
    {
        switch (type)
        {
            case null:
                return null;
            case IArrayTypeSymbol array:
                return FindForeignInterior(array.ElementType, owner);
            case INamedTypeSymbol named:
                if (IsForeignInterior(DuneSymbols.NamespaceOf(named.OriginalDefinition), owner))
                {
                    return named;
                }

                foreach (var argument in named.TypeArguments)
                {
                    var found = FindForeignInterior(argument, owner);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static bool IsForeignInterior(string ns, string owner)
    {
        if (!ns.StartsWith(FeaturesNamespace, System.StringComparison.Ordinal))
        {
            return false;
        }

        var rest = ns.Substring(FeaturesNamespace.Length);
        var dot = rest.IndexOf('.');
        var feature = dot < 0 ? rest : rest.Substring(0, dot);
        var tail = dot < 0 ? string.Empty : rest.Substring(dot + 1);
        if (feature.Length == 0 || feature.Equals(owner, System.StringComparison.Ordinal))
        {
            return false;
        }

        return !tail.Equals("Contracts", System.StringComparison.Ordinal)
            && !tail.StartsWith("Contracts.", System.StringComparison.Ordinal);
    }
}
