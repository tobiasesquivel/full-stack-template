using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BusBoundaryAnalyzer : DiagnosticAnalyzer
{
    private const string BusHome = "backend/Infrastructure/Messaging/";
    private static readonly string[] BusNamespaces = { "Wolverine", "JasperFx" };
    private static readonly string[] OrchestratorBases = { "Wolverine.Saga", "Wolverine.TimeoutMessage" };
    private const string MediatorBus = "Wolverine.IMessageBus";
    private static readonly string[] MediatorBuses = { MediatorBus, "Wolverine.ICommandBus" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.BusOutsideMessaging);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        SyntaxNode qualifier = name.Parent is AliasQualifiedNameSyntax alias && alias.Name == name ? alias : name;
        if (qualifier.Parent is QualifiedNameSyntax qualified && qualified.Left == qualifier)
        {
            return;
        }

        var path = DunePaths.Normalize(name.SyntaxTree.FilePath);
        if (!IsChecked(path) || DunePaths.ContainsOrdinal(path, BusHome) || DeclaresOrchestrator(context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        if (DunePaths.IsSliceEndpoint(path) && IsMediatorName(name, symbol))
        {
            return;
        }

        var ns = symbol switch
        {
            null => string.Empty,
            INamespaceSymbol namespaceSymbol => namespaceSymbol.ToDisplayString(),
            _ => DuneSymbols.NamespaceOf(symbol.OriginalDefinition),
        };
        if (IsBusNamespace(ns))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.BusOutsideMessaging,
                name.GetLocation(),
                name.ToString(),
                "fuera de backend/Infrastructure/Messaging"));
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (method.Name.StartsWith("Invoke", StringComparison.Ordinal)
            && IsBusNamespace(DuneSymbols.NamespaceOf(method.ContainingType.OriginalDefinition))
            && IsChecked(path)
            && !(DunePaths.IsSliceEndpoint(path) && IsMediatorDispatch(method)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.BusOutsideMessaging,
                invocation.Syntax.GetLocation(),
                method.Name,
                "como mediator"));
        }
    }

    public static bool IsMediatorDispatch(IMethodSymbol method) =>
        method.Name.Equals("InvokeAsync", StringComparison.Ordinal)
        && method.Arity == 1
        && Array.IndexOf(MediatorBuses, method.ContainingType.OriginalDefinition.ToDisplayString()) >= 0
        && method.Parameters.All(parameter => !parameter.Type.ToDisplayString().Equals("Wolverine.DeliveryOptions", StringComparison.Ordinal));

    private static bool IsMediatorName(SimpleNameSyntax name, ISymbol? symbol) => symbol switch
    {
        INamespaceSymbol ns => ns.ToDisplayString().Equals("Wolverine", StringComparison.Ordinal) && name.Parent is UsingDirectiveSyntax,
        INamedTypeSymbol type => type.ToDisplayString().Equals(MediatorBus, StringComparison.Ordinal),
        IMethodSymbol method => IsMediatorDispatch(method),
        _ => false,
    };

    private static bool DeclaresOrchestrator(SemanticModel model, System.Threading.CancellationToken ct)
    {
        foreach (var declaration in model.SyntaxTree.GetRoot(ct).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            for (var type = (model.GetDeclaredSymbol(declaration, ct) as INamedTypeSymbol)?.BaseType; type is not null; type = type.BaseType)
            {
                if (Array.IndexOf(OrchestratorBases, type.OriginalDefinition.ToDisplayString()) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsChecked(string path) =>
        DunePaths.IsBackendFile(path) && !DunePaths.IsMigration(path) && !DunePaths.IsTestsFile(path);

    private static bool IsBusNamespace(string ns)
    {
        foreach (var root in BusNamespaces)
        {
            if (ns.Equals(root, StringComparison.Ordinal) || ns.StartsWith(root + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
