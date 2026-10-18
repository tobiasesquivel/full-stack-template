using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationAccessAnalyzer : DiagnosticAnalyzer
{
    private const string ConfigurationInterface = "Microsoft.Extensions.Configuration.IConfiguration";

    private static readonly ImmutableHashSet<string> EnvironmentReads = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "GetEnvironmentVariable",
        "GetEnvironmentVariables",
        "ExpandEnvironmentVariables");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ConfigurationAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IdentifierName);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        var path = DunePaths.Normalize(node.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
        if (symbol is IMethodSymbol method && IsEnvironmentRead(method))
        {
            Report(context, node, $"Environment.{method.Name}");
            return;
        }

        var type = symbol as ITypeSymbol ?? context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type;
        var configuration = FindConfigurationType(type);
        if (configuration is not null)
        {
            Report(context, node, configuration.ToDisplayString());
        }
    }

    private static bool IsEnvironmentRead(IMethodSymbol method) =>
        EnvironmentReads.Contains(method.Name)
        && method.ContainingType.ToDisplayString().Equals("System.Environment", StringComparison.Ordinal);

    private static ITypeSymbol? FindConfigurationType(ITypeSymbol? type)
    {
        switch (type)
        {
            case null:
                return null;
            case IArrayTypeSymbol array:
                return FindConfigurationType(array.ElementType);
            case INamedTypeSymbol named:
                if (IsConfiguration(named))
                {
                    return named;
                }

                return named.TypeArguments.Select(FindConfigurationType).FirstOrDefault(found => found is not null);
            default:
                return null;
        }
    }

    private static bool IsConfiguration(INamedTypeSymbol type) =>
        type.ToDisplayString().Equals(ConfigurationInterface, StringComparison.Ordinal)
        || type.AllInterfaces.Any(contract => contract.ToDisplayString().Equals(ConfigurationInterface, StringComparison.Ordinal));

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, string name) =>
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.ConfigurationAccess,
            node.GetLocation(),
            name));
}
