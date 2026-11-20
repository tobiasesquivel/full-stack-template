using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpClientBoundaryAnalyzer : DiagnosticAnalyzer
{
    private const string HttpHome = "backend/Infrastructure/Http/";
    private static readonly string[] ResilienceNamespaces = { "Polly", "Microsoft.Extensions.Http.Resilience" };
    private static readonly string[] HttpPipelineTypes = { "System.Net.Http.HttpMessageInvoker", "System.Net.Http.HttpMessageHandler" };
    private const string ClientFactory = "System.Net.Http.IHttpClientFactory";
    private static readonly string[] ClientRegistrationTypes =
    {
        "Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions",
        "Microsoft.Extensions.DependencyInjection.HttpClientBuilderExtensions",
        "Microsoft.Extensions.DependencyInjection.ResilienceHttpClientBuilderExtensions",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.HttpClientOutsideInfrastructure);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
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

        if (!IsChecked(name.SyntaxTree.FilePath))
        {
            return;
        }

        var info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        var ns = symbol switch
        {
            null => string.Empty,
            INamespaceSymbol namespaceSymbol => namespaceSymbol.ToDisplayString(),
            _ => DuneSymbols.NamespaceOf(symbol.OriginalDefinition),
        };
        if (IsInNamespace(ns, ResilienceNamespaces))
        {
            Report(context.ReportDiagnostic, name.GetLocation(), name.ToString(), "nombra la librería de resiliencia");
        }
        else if (symbol is INamedTypeSymbol type && type.OriginalDefinition.ToDisplayString() == ClientFactory)
        {
            Report(context.ReportDiagnostic, name.GetLocation(), name.ToString(), "pide clientes a la fábrica sin pasar por AddRemoteClient");
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is INamedTypeSymbol type
            && DerivesFromAny(type, HttpPipelineTypes)
            && IsChecked(creation.Syntax.SyntaxTree.FilePath))
        {
            Report(context.ReportDiagnostic, creation.Syntax.GetLocation(), type.Name, "arma un cliente o un handler HTTP a mano");
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (Array.IndexOf(ClientRegistrationTypes, method.ContainingType.OriginalDefinition.ToDisplayString()) >= 0
            && IsChecked(invocation.Syntax.SyntaxTree.FilePath))
        {
            Report(context.ReportDiagnostic, invocation.Syntax.GetLocation(), method.Name, "registra un cliente HTTP sin AddRemoteClient");
        }
    }

    private static void Report(Action<Diagnostic> report, Location location, string name, string reason) =>
        report(Diagnostic.Create(DuneDiagnostics.HttpClientOutsideInfrastructure, location, name, reason));

    private static bool DerivesFromAny(INamedTypeSymbol type, string[] bases)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (Array.IndexOf(bases, current.OriginalDefinition.ToDisplayString()) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsChecked(string? filePath)
    {
        var path = DunePaths.Normalize(filePath);
        return DunePaths.IsBackendFile(path)
            && !DunePaths.IsMigration(path)
            && !DunePaths.IsTestsFile(path)
            && !DunePaths.ContainsOrdinal(path, HttpHome);
    }

    private static bool IsInNamespace(string ns, string[] roots)
    {
        foreach (var root in roots)
        {
            if (ns.Equals(root, StringComparison.Ordinal) || ns.StartsWith(root + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
