using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ApiRouteAnalyzer : DiagnosticAnalyzer
{
    private const string ApiPrefix = "/api";
    private const string InternalPrefix = "/internal/v1";
    private const string InternalApiFile = "backend/Infrastructure/Http/InternalApi.cs";

    private static readonly ImmutableHashSet<string> UnsafeMaps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "MapPost",
        "MapPut",
        "MapPatch",
        "MapDelete",
        "MapMethods",
        "Map");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ApiRoute);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
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
        if (IsAspNetRouteMap(method))
        {
            AnalyzeRouteMap(context, invocation, method, path);
            return;
        }

        if (IsSliceEndpointMap(method))
        {
            AnalyzeSliceMap(context, invocation);
        }
    }

    private static void AnalyzeRouteMap(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IMethodSymbol method,
        string path)
    {
        var receiver = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        if (receiver is null || IsRouteGroup(StaticType(receiver)))
        {
            return;
        }

        if (method.Name.Equals("MapGroup", StringComparison.Ordinal))
        {
            var prefix = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value;
            if (prefix?.ConstantValue is { HasValue: true, Value: string text }
                && (IsUnderApi(text) || IsInternalGroup(text, path)))
            {
                return;
            }

            Report(context, invocation, "Un grupo del host empieza con /api y su prefijo es un literal");
            return;
        }

        if (UnsafeMaps.Contains(method.Name) && !DunePaths.IsEndpointFile(path, Path.GetFileName(path)))
        {
            Report(context, invocation, $"{method.Name} va en la slice, sobre el grupo de su feature, no sobre el host");
        }
    }

    private static void AnalyzeSliceMap(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var argument = invocation.Arguments.FirstOrDefault(candidate => candidate.Parameter?.Ordinal == 0)?.Value;
        if (argument is null || IsRouteGroup(StaticType(argument)))
        {
            return;
        }

        Report(context, invocation, $"{invocation.TargetMethod.ContainingType.Name}.Map recibe el RouteGroupBuilder de su feature, no el host");
    }

    private static ITypeSymbol? StaticType(IOperation operation)
    {
        while (operation is IConversionOperation conversion && conversion.IsImplicit)
        {
            operation = conversion.Operand;
        }

        return operation.Type;
    }

    private static bool IsAspNetRouteMap(IMethodSymbol method) =>
        method.IsExtensionMethod
        && (method.Name.Equals("MapGroup", StringComparison.Ordinal) || UnsafeMaps.Contains(method.Name))
        && DuneSymbols.InNamespace(method.ContainingType, "Microsoft.AspNetCore");

    private static bool IsSliceEndpointMap(IMethodSymbol method) =>
        method.IsStatic
        && method.Name.Equals("Map", StringComparison.Ordinal)
        && method.Parameters.Length == 1
        && method.ContainingType.Name.EndsWith("Endpoint", StringComparison.Ordinal)
        && DuneSymbols.InNamespace(method.ContainingType, "TemplateApp.Features");

    private static bool IsRouteGroup(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.Name.Equals("RouteGroupBuilder", StringComparison.Ordinal)
        && DuneSymbols.NamespaceOf(named).Equals("Microsoft.AspNetCore.Routing", StringComparison.Ordinal);

    private static bool IsUnderApi(string prefix) =>
        prefix.Equals(ApiPrefix, StringComparison.Ordinal)
        || prefix.StartsWith(ApiPrefix + "/", StringComparison.Ordinal);

    private static bool IsInternalGroup(string prefix, string path) =>
        prefix.Equals(InternalPrefix, StringComparison.Ordinal)
        && path.EndsWith(InternalApiFile, StringComparison.Ordinal);

    private static void Report(OperationAnalysisContext context, IInvocationOperation invocation, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.ApiRoute,
            invocation.Syntax.GetLocation(),
            reason));
}
