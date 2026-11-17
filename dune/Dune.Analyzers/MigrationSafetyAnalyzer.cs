using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationSafetyAnalyzer : DiagnosticAnalyzer
{
    private const string MigrationsNamespace = "Microsoft.EntityFrameworkCore.Migrations";
    private const string LastMigrationBeforeRule = "20261117132830";
    private const string ContractPrefix = "Contract";
    private const string ConcurrentIndexAnnotation = "Npgsql:CreatedConcurrently";

    private static readonly ImmutableHashSet<string> Renames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "RenameColumn",
        "RenameTable");

    private static readonly ImmutableHashSet<string> Drops = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "DropColumn",
        "DropTable",
        "DropIndex",
        "DropSequence",
        "DropSchema");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DuneDiagnostics.MigrationRename,
            DuneDiagnostics.MigrationDropOutsideContract,
            DuneDiagnostics.MigrationContractMixed,
            DuneDiagnostics.MigrationIndexNotConcurrent,
            DuneDiagnostics.MigrationNotNullOnExisting);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockAction(AnalyzeUp);
    }

    private static void AnalyzeUp(OperationBlockAnalysisContext context)
    {
        if (!(context.OwningSymbol is IMethodSymbol { Name: "Up", IsOverride: true } up)
            || !DerivesFromMigration(up.ContainingType)
            || !IsNewMigration(up.ContainingType))
        {
            return;
        }

        var path = DunePaths.Normalize(up.Locations.FirstOrDefault()?.SourceTree?.FilePath);
        if (!DunePaths.IsBackendFile(path) || !DunePaths.IsMigration(path))
        {
            return;
        }

        var operations = context.OperationBlocks
            .SelectMany(block => block.DescendantsAndSelf())
            .OfType<IInvocationOperation>()
            .Where(IsBuilderOperation)
            .ToList();
        var createdTables = new HashSet<string>(
            operations
                .Where(operation => operation.TargetMethod.Name == "CreateTable")
                .Select(operation => Argument(operation, "name") as string)
                .Where(name => name is not null)
                .Select(name => name!),
            StringComparer.Ordinal);
        var migration = up.ContainingType.Name;
        var isContract = migration.StartsWith(ContractPrefix, StringComparison.Ordinal);

        foreach (var operation in operations)
        {
            var rule = Violation(operation, isContract, createdTables);
            if (rule is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                rule,
                operation.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add("operation", operation.TargetMethod.Name),
                migration,
                operation.TargetMethod.Name));
        }
    }

    private static DiagnosticDescriptor? Violation(IInvocationOperation operation, bool isContract, HashSet<string> createdTables)
    {
        var name = operation.TargetMethod.Name;
        if (Renames.Contains(name))
        {
            return DuneDiagnostics.MigrationRename;
        }

        var drops = Drops.Contains(name);
        if (drops != isContract)
        {
            return drops ? DuneDiagnostics.MigrationDropOutsideContract : DuneDiagnostics.MigrationContractMixed;
        }

        if (name == "CreateIndex"
            && !(Argument(operation, "table") is string table && createdTables.Contains(table))
            && !IsCreatedConcurrently(operation))
        {
            return DuneDiagnostics.MigrationIndexNotConcurrent;
        }

        if (name == "AlterColumn"
            && Argument(operation, "nullable") is false
            && Argument(operation, "oldNullable") is true)
        {
            return DuneDiagnostics.MigrationNotNullOnExisting;
        }

        return null;
    }

    private static bool IsBuilderOperation(IInvocationOperation invocation) =>
        invocation.TargetMethod.ContainingType.Name == "MigrationBuilder"
        && DuneSymbols.NamespaceOf(invocation.TargetMethod.ContainingType).Equals(MigrationsNamespace, StringComparison.Ordinal);

    private static object? Argument(IInvocationOperation invocation, string parameter)
    {
        var argument = invocation.Arguments.FirstOrDefault(candidate => candidate.Parameter?.Name == parameter);
        if (argument is null)
        {
            return null;
        }

        return ConstantOf(argument.Value);
    }

    private static object? ConstantOf(IOperation value)
    {
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value.ConstantValue.HasValue ? value.ConstantValue.Value : null;
    }

    private static bool IsCreatedConcurrently(IInvocationOperation createIndex)
    {
        IOperation current = createIndex;
        while (current.Parent is IInvocationOperation annotation
            && annotation.TargetMethod.Name == "Annotation"
            && ReferenceEquals(annotation.Instance, current))
        {
            if (annotation.Arguments.Length == 2
                && ConstantOf(annotation.Arguments[0].Value) is ConcurrentIndexAnnotation
                && ConstantOf(annotation.Arguments[1].Value) is true)
            {
                return true;
            }

            current = annotation;
        }

        return false;
    }

    private static bool DerivesFromMigration(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "Migration"
                && DuneSymbols.NamespaceOf(current).Equals(MigrationsNamespace, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNewMigration(INamedTypeSymbol type)
    {
        var id = type.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.Name == "MigrationAttribute"
                && DuneSymbols.NamespaceOf(attribute.AttributeClass).Equals(MigrationsNamespace, StringComparison.Ordinal))
            .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .FirstOrDefault();
        return id is null
            || id.Length < LastMigrationBeforeRule.Length
            || string.CompareOrdinal(id.Substring(0, LastMigrationBeforeRule.Length), LastMigrationBeforeRule) > 0;
    }
}
