using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RemoteReadAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> WriteVerbs = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "MapPost",
        "MapPut",
        "MapDelete",
        "MapPatch");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ReadSliceCallsRemote);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var getSlices = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var writeSlices = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var findings = new ConcurrentBag<(string Slice, Diagnostic Diagnostic)>();

            start.RegisterOperationAction(
                operation => CollectVerb(operation, getSlices, writeSlices),
                OperationKind.Invocation);
            start.RegisterSymbolAction(
                symbol => AnalyzeType(symbol, findings),
                SymbolKind.NamedType);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (var finding in findings)
                {
                    if (getSlices.ContainsKey(finding.Slice) && !writeSlices.ContainsKey(finding.Slice))
                    {
                        end.ReportDiagnostic(finding.Diagnostic);
                    }
                }
            });
        });
    }

    private static void CollectVerb(
        OperationAnalysisContext context,
        ConcurrentDictionary<string, byte> getSlices,
        ConcurrentDictionary<string, byte> writeSlices)
    {
        if (!TrySliceKey(context.Operation.Syntax.SyntaxTree.FilePath, out var key, out _))
        {
            return;
        }

        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        if (!DuneSymbols.InNamespace(method.ContainingType, "Microsoft.AspNetCore"))
        {
            return;
        }

        if (method.Name.Equals("MapGet", StringComparison.Ordinal))
        {
            getSlices.TryAdd(key, 0);
        }
        else if (WriteVerbs.Contains(method.Name))
        {
            writeSlices.TryAdd(key, 0);
        }
    }

    private static void AnalyzeType(SymbolAnalysisContext context, ConcurrentBag<(string Slice, Diagnostic Diagnostic)> findings)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location is null || !TrySliceKey(location.SourceTree!.FilePath, out var key, out var feature))
        {
            return;
        }

        var chain = RemoteChain(type, "TemplateApp.Features." + feature, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        if (chain is null)
        {
            return;
        }

        findings.Add((key, Diagnostic.Create(
            DuneDiagnostics.ReadSliceCallsRemote,
            location,
            key,
            string.Join(" -> ", chain.Prepend(type).Select(link => link.Name)))));
    }

    private static List<ITypeSymbol>? RemoteChain(INamedTypeSymbol type, string featureNamespace, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return null;
        }

        foreach (var parameter in type.InstanceConstructors.SelectMany(constructor => constructor.Parameters))
        {
            var dependency = parameter.Type;
            if (DuneSymbols.IsHttpInvoker(dependency) || DuneSymbols.IsTypedClient(dependency))
            {
                return new List<ITypeSymbol> { dependency };
            }

            if (dependency is INamedTypeSymbol { TypeKind: TypeKind.Class } named
                && DuneSymbols.InNamespace(named, featureNamespace)
                && RemoteChain(named, featureNamespace, visited) is { } rest)
            {
                rest.Insert(0, named);
                return rest;
            }
        }

        return null;
    }

    private static bool TrySliceKey(string? filePath, out string key, out string feature)
    {
        key = string.Empty;
        var path = DunePaths.Normalize(filePath);
        if (DunePaths.IsTestsFile(path)
            || !DunePaths.TryGetFeature(path, out feature)
            || !DunePaths.TryGetSlice(path, out var slice))
        {
            feature = string.Empty;
            return false;
        }

        key = feature + "/" + slice;
        return true;
    }
}
