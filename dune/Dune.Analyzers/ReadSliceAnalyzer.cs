using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReadSliceAnalyzer : DiagnosticAnalyzer
{
    private const string EfNamespace = "Microsoft.EntityFrameworkCore";

    private static readonly ImmutableHashSet<string> EntityLoadExceptions = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Users/GetUser");

    private static readonly ImmutableHashSet<string> WriteVerbs = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "MapPost",
        "MapPut",
        "MapDelete",
        "MapPatch");

    private static readonly ImmutableHashSet<string> BulkWrites = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ExecuteUpdate",
        "ExecuteUpdateAsync",
        "ExecuteDelete",
        "ExecuteDeleteAsync");

    private static readonly ImmutableHashSet<string> TrackedWrites = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "SaveChanges",
        "SaveChangesAsync",
        "Add",
        "AddAsync",
        "AddRange",
        "AddRangeAsync",
        "Update",
        "UpdateRange",
        "Remove",
        "RemoveRange",
        "Attach",
        "AttachRange");

    private static readonly ImmutableHashSet<string> Materializers = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "Last",
        "LastAsync",
        "LastOrDefault",
        "LastOrDefaultAsync",
        "ElementAt",
        "ElementAtOrDefault",
        "AsAsyncEnumerable",
        "Load",
        "LoadAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ReadSliceWrites, DuneDiagnostics.ReadSliceLoadsEntity);

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
                operation => AnalyzeInvocation(operation, getSlices, writeSlices, findings),
                OperationKind.Invocation);
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

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        ConcurrentDictionary<string, byte> getSlices,
        ConcurrentDictionary<string, byte> writeSlices,
        ConcurrentBag<(string Slice, Diagnostic Diagnostic)> findings)
    {
        var path = DunePaths.Normalize(context.Operation.Syntax.SyntaxTree.FilePath);
        if (DunePaths.IsTestsFile(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || !DunePaths.TryGetSlice(path, out var slice))
        {
            return;
        }

        var key = feature + "/" + slice;
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (DuneSymbols.InNamespace(method.ContainingType, "Microsoft.AspNetCore"))
        {
            if (method.Name.Equals("MapGet", StringComparison.Ordinal))
            {
                getSlices.TryAdd(key, 0);
            }
            else if (WriteVerbs.Contains(method.Name))
            {
                writeSlices.TryAdd(key, 0);
            }

            return;
        }

        if (IsWrite(method, context.Compilation))
        {
            findings.Add((key, Diagnostic.Create(
                DuneDiagnostics.ReadSliceWrites,
                invocation.Syntax.GetLocation(),
                key,
                method.Name)));
            return;
        }

        if (!EntityLoadExceptions.Contains(key) && LoadedEntity(invocation) is { } entity)
        {
            findings.Add((key, Diagnostic.Create(
                DuneDiagnostics.ReadSliceLoadsEntity,
                invocation.Syntax.GetLocation(),
                key,
                entity.Name,
                method.Name)));
        }
    }

    private static bool IsWrite(IMethodSymbol method, Compilation compilation)
    {
        if (BulkWrites.Contains(method.Name))
        {
            return DuneSymbols.InNamespace(method.ContainingType, EfNamespace);
        }

        return TrackedWrites.Contains(method.Name)
            && (DuneSymbols.IsDbSet(method.ContainingType) || IsDbContext(method.ContainingType, compilation));
    }

    private static bool IsDbContext(INamedTypeSymbol type, Compilation compilation) =>
        (type.Name.Equals("DbContext", StringComparison.Ordinal)
            && DuneSymbols.NamespaceOf(type).Equals(EfNamespace, StringComparison.Ordinal))
        || DuneSymbols.DerivesFromDbContext(type, compilation);

    private static ITypeSymbol? LoadedEntity(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if ((method.Name.Equals("Find", StringComparison.Ordinal) || method.Name.Equals("FindAsync", StringComparison.Ordinal))
            && DuneSymbols.IsDbSet(method.ContainingType))
        {
            return AsDomainType(method.ContainingType.TypeArguments[0]);
        }

        if (!Materializers.Contains(method.Name)
            || !method.IsGenericMethod
            || !(DuneSymbols.InNamespace(method.ContainingType, EfNamespace) || DuneSymbols.InNamespace(method.ContainingType, "System.Linq"))
            || invocation.Arguments.Length == 0
            || !IsQueryable(invocation.Arguments[0].Value))
        {
            return null;
        }

        return AsDomainType(method.TypeArguments[0]);
    }

    private static bool IsQueryable(IOperation receiver)
    {
        while (receiver is IConversionOperation { IsImplicit: true } conversion)
        {
            receiver = conversion.Operand;
        }

        return receiver.Type is { } type
            && (IsQueryableInterface(type) || type.AllInterfaces.Any(IsQueryableInterface));
    }

    private static bool IsQueryableInterface(ITypeSymbol type) =>
        type.Name.Equals("IQueryable", StringComparison.Ordinal)
        && DuneSymbols.NamespaceOf(type).Equals("System.Linq", StringComparison.Ordinal);

    private static ITypeSymbol? AsDomainType(ITypeSymbol type)
    {
        var ns = DuneSymbols.NamespaceOf(type);
        return ns.StartsWith("TemplateApp.Features.", StringComparison.Ordinal) && ns.EndsWith(".Domain", StringComparison.Ordinal)
            ? type
            : null;
    }
}
