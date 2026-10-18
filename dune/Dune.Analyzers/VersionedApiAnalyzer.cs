using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VersionedApiAnalyzer : DiagnosticAnalyzer
{
    private const string ApiPrefix = "/api";
    private const string VersionedPrefix = "/api/v{version:apiVersion}";

    private static readonly ImmutableHashSet<string> VersionSetFactories = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "NewVersionedApi",
        "WithApiVersionSet",
        "NewApiVersionSet");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.VersionedApi);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterSymbolAction(AnalyzeFeatureMap, SymbolKind.Method);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path))
        {
            return;
        }

        var method = invocation.TargetMethod;
        if (!method.IsExtensionMethod || !DuneSymbols.InNamespace(method.ContainingType, "Microsoft.AspNetCore"))
        {
            return;
        }

        if (VersionSetFactories.Contains(method.Name))
        {
            if (!DunePaths.IsInfrastructureFile(path))
            {
                Report(context, invocation, $"{method.Name} va solo en Infrastructure; la feature recibe el grupo versionado");
            }

            return;
        }

        if (method.Name.Equals("MapGroup", StringComparison.Ordinal))
        {
            AnalyzeMapGroup(context, invocation);
        }
    }

    private static void AnalyzeMapGroup(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var receiver = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        var receiverType = receiver is null ? null : StaticType(receiver);
        if (receiverType is null || IsNamed(receiverType, "Microsoft.AspNetCore.Routing", "RouteGroupBuilder"))
        {
            return;
        }

        var prefix = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value;
        var text = prefix?.ConstantValue is { HasValue: true, Value: string value } ? value : null;

        if (IsNamed(receiverType, "Asp.Versioning.Builder", "IVersionedEndpointRouteBuilder"))
        {
            if (!string.Equals(text, VersionedPrefix, StringComparison.Ordinal))
            {
                Report(context, invocation, $"El grupo versionado usa el prefijo literal {VersionedPrefix}");
            }

            return;
        }

        if (text is not null && IsUnderApi(text))
        {
            Report(context, invocation, "Un grupo de /api sobre el host queda sin versión");
        }
    }

    private static void AnalyzeFeatureMap(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        var type = method.ContainingType;
        if (!method.IsStatic
            || !method.Name.StartsWith("Map", StringComparison.Ordinal)
            || !method.Name.EndsWith("Endpoints", StringComparison.Ordinal)
            || type is null
            || !type.Name.EndsWith("Feature", StringComparison.Ordinal)
            || !DuneSymbols.InNamespace(type, "TemplateApp.Features"))
        {
            return;
        }

        var first = method.Parameters.FirstOrDefault();
        if (first is not null && IsNamed(first.Type, "Microsoft.AspNetCore.Routing", "RouteGroupBuilder"))
        {
            return;
        }

        foreach (var location in method.Locations.Where(location => location.IsInSource))
        {
            var path = DunePaths.Normalize(location.SourceTree?.FilePath ?? string.Empty);
            if (!DunePaths.IsBackendFile(path) || DunePaths.IsTestsFile(path))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.VersionedApi,
                location,
                $"{type.Name}.{method.Name} recibe el RouteGroupBuilder versionado, no el host"));
        }
    }

    private static ITypeSymbol? StaticType(IOperation operation)
    {
        while (operation is IConversionOperation conversion && conversion.IsImplicit)
        {
            operation = conversion.Operand;
        }

        return operation.Type;
    }

    private static bool IsNamed(ITypeSymbol type, string ns, string name) =>
        type is INamedTypeSymbol named
        && named.Name.Equals(name, StringComparison.Ordinal)
        && DuneSymbols.NamespaceOf(named).Equals(ns, StringComparison.Ordinal);

    private static bool IsUnderApi(string prefix) =>
        prefix.Equals(ApiPrefix, StringComparison.Ordinal)
        || prefix.StartsWith(ApiPrefix + "/", StringComparison.Ordinal);

    private static void Report(OperationAnalysisContext context, IInvocationOperation invocation, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.VersionedApi,
            invocation.Syntax.GetLocation(),
            reason));
}
