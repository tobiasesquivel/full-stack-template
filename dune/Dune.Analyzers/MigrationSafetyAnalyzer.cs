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
    private const string MovePrefix = "Move";
    private const string DefaultSchema = "public";
    private const string ConcurrentIndexAnnotation = "Npgsql:CreatedConcurrently";

    private static readonly ImmutableHashSet<string> Drops = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "DropColumn",
        "DropTable",
        "DropIndex",
        "DropSequence",
        "DropSchema");

    private static readonly ImmutableHashSet<string> ConstraintDrops = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "DropForeignKey",
        "DropPrimaryKey",
        "DropUniqueConstraint",
        "DropCheckConstraint");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DuneDiagnostics.MigrationRename,
            DuneDiagnostics.MigrationDropOutsideContract,
            DuneDiagnostics.MigrationContractMixed,
            DuneDiagnostics.MigrationIndexNotConcurrent,
            DuneDiagnostics.MigrationNotNullOnExisting,
            DuneDiagnostics.MigrationTypeChange,
            DuneDiagnostics.MigrationMoveOutsideMove,
            DuneDiagnostics.MigrationMoveMixed,
            DuneDiagnostics.MigrationMoveWithoutWayBack,
            DuneDiagnostics.MigrationMoveWithoutSchema);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolStartAction(StartMigration, SymbolKind.NamedType);
    }

    private static void StartMigration(SymbolStartAnalysisContext context)
    {
        var migration = (INamedTypeSymbol)context.Symbol;
        if (!DerivesFromMigration(migration) || !IsNewMigration(migration))
        {
            return;
        }

        var bodies = new MigrationBodies();
        context.RegisterOperationBlockAction(bodies.Collect);
        context.RegisterSymbolEndAction(end => Report(end, migration.Name, bodies.Up, bodies.Down));
    }

    private static void Report(
        SymbolAnalysisContext context,
        string migration,
        IReadOnlyList<IInvocationOperation> up,
        IReadOnlyList<IInvocationOperation> down)
    {
        var isContract = migration.StartsWith(ContractPrefix, StringComparison.Ordinal);
        var isMove = IsMoveMigration(migration);
        var createdTables = NamesOf(up, "CreateTable");

        foreach (var operation in up)
        {
            ReportIfAny(context, Violation(operation, isContract, isMove, createdTables), operation, migration);
        }

        if (!isMove)
        {
            return;
        }

        var moves = up.Where(IsSchemaMove).ToList();
        var ensured = NamesOf(up, "EnsureSchema");

        foreach (var operation in down)
        {
            if (operation.TargetMethod.Name != "RenameTable")
            {
                ReportIfAny(context, DuneDiagnostics.MigrationMoveMixed, operation, migration);
            }
            else if (!moves.Any(move => Reverses(operation, move)))
            {
                ReportIfAny(context, DuneDiagnostics.MigrationMoveWithoutWayBack, operation, migration);
            }
        }

        foreach (var move in moves)
        {
            var table = Argument(move, "name") as string;
            var schema = Argument(move, "newSchema") as string;
            var undone = down.Any(operation => operation.TargetMethod.Name == "RenameTable"
                && Argument(operation, "name") as string == table
                && Argument(operation, "schema") as string == schema);
            if (!undone)
            {
                ReportIfAny(context, DuneDiagnostics.MigrationMoveWithoutWayBack, move, migration);
            }

            if (schema != DefaultSchema && !ensured.Contains(schema!))
            {
                ReportIfAny(context, DuneDiagnostics.MigrationMoveWithoutSchema, move, migration);
            }
        }
    }

    private static HashSet<string> NamesOf(IReadOnlyList<IInvocationOperation> operations, string method) =>
        new(
            operations.Where(operation => operation.TargetMethod.Name == method)
                .Select(operation => Argument(operation, "name") as string)
                .Where(name => name is not null)
                .Select(name => name!),
            StringComparer.Ordinal);

    private static bool IsMoveMigration(string migration) =>
        migration.StartsWith(MovePrefix, StringComparison.Ordinal)
        && (migration.Length == MovePrefix.Length || char.IsUpper(migration[MovePrefix.Length]));

    private static bool Reverses(IInvocationOperation back, IInvocationOperation move) =>
        IsSchemaMove(back)
        && Argument(back, "name") as string == Argument(move, "name") as string
        && Argument(back, "schema") as string == Argument(move, "newSchema") as string
        && Argument(back, "newSchema") as string == (Argument(move, "schema") as string ?? DefaultSchema);

    private static void ReportIfAny(SymbolAnalysisContext context, DiagnosticDescriptor? rule, IInvocationOperation operation, string migration)
    {
        if (rule is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            rule,
            operation.Syntax.GetLocation(),
            ImmutableDictionary<string, string?>.Empty.Add("operation", operation.TargetMethod.Name),
            migration,
            operation.TargetMethod.Name));
    }

    private static DiagnosticDescriptor? Violation(IInvocationOperation operation, bool isContract, bool isMove, HashSet<string> createdTables)
    {
        var name = operation.TargetMethod.Name;
        if (name == "RenameColumn" || (name == "RenameTable" && !IsSchemaMove(operation)))
        {
            return DuneDiagnostics.MigrationRename;
        }

        if (name == "RenameTable")
        {
            return isMove ? null : DuneDiagnostics.MigrationMoveOutsideMove;
        }

        if (isMove)
        {
            return name == "EnsureSchema" ? null : DuneDiagnostics.MigrationMoveMixed;
        }

        if (ConstraintDrops.Contains(name))
        {
            return null;
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

        if (name == "AlterColumn"
            && Argument(operation, "type") is string type
            && Argument(operation, "oldType") is string oldType
            && !string.Equals(type, oldType, StringComparison.Ordinal))
        {
            return DuneDiagnostics.MigrationTypeChange;
        }

        return null;
    }

    private static bool IsSchemaMove(IInvocationOperation operation) =>
        operation.TargetMethod.Name == "RenameTable"
        && Argument(operation, "name") is string table
        && TryConstant(operation, "newName", out var newName)
        && (newName is null || newName as string == table)
        && Argument(operation, "newSchema") is string newSchema
        && !string.Equals(newSchema, Argument(operation, "schema") as string, StringComparison.Ordinal);

    private static bool IsBuilderOperation(IInvocationOperation invocation) =>
        invocation.TargetMethod.ContainingType.Name == "MigrationBuilder"
        && DuneSymbols.NamespaceOf(invocation.TargetMethod.ContainingType).Equals(MigrationsNamespace, StringComparison.Ordinal);

    private static object? Argument(IInvocationOperation invocation, string parameter) =>
        TryConstant(invocation, parameter, out var value) ? value : null;

    private static bool TryConstant(IInvocationOperation invocation, string parameter, out object? value)
    {
        value = null;
        var argument = invocation.Arguments.FirstOrDefault(candidate => candidate.Parameter?.Name == parameter);
        if (argument is null)
        {
            return false;
        }

        var constant = Unwrap(argument.Value).ConstantValue;
        value = constant.HasValue ? constant.Value : null;
        return constant.HasValue;
    }

    private static object? ConstantOf(IOperation value)
    {
        var constant = Unwrap(value).ConstantValue;
        return constant.HasValue ? constant.Value : null;
    }

    private static IOperation Unwrap(IOperation value)
    {
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value;
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

    private sealed class MigrationBodies
    {
        private readonly object _gate = new();
        private readonly List<IInvocationOperation> _up = new();
        private readonly List<IInvocationOperation> _down = new();

        public IReadOnlyList<IInvocationOperation> Up => Snapshot(_up);

        public IReadOnlyList<IInvocationOperation> Down => Snapshot(_down);

        public void Collect(OperationBlockAnalysisContext context)
        {
            if (!(context.OwningSymbol is IMethodSymbol { IsOverride: true, Name: "Up" or "Down" } method))
            {
                return;
            }

            var path = DunePaths.Normalize(method.Locations.FirstOrDefault()?.SourceTree?.FilePath);
            if (!DunePaths.IsBackendFile(path) || !DunePaths.IsMigration(path))
            {
                return;
            }

            var operations = context.OperationBlocks
                .SelectMany(block => block.DescendantsAndSelf())
                .OfType<IInvocationOperation>()
                .Where(IsBuilderOperation)
                .ToList();
            lock (_gate)
            {
                (method.Name == "Up" ? _up : _down).AddRange(operations);
            }
        }

        private IReadOnlyList<IInvocationOperation> Snapshot(List<IInvocationOperation> operations)
        {
            lock (_gate)
            {
                return operations.ToList();
            }
        }
    }
}
