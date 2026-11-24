using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionOwnerAnalyzer : DiagnosticAnalyzer
{
    private const string EfNamespace = "Microsoft.EntityFrameworkCore";

    private static readonly string[] OwnedFeatures = { "Auth", "Users", "Projects" };

    private static readonly ImmutableHashSet<string> FacadeTransactions = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "BeginTransaction",
        "BeginTransactionAsync",
        "CommitTransaction",
        "CommitTransactionAsync",
        "RollbackTransaction",
        "RollbackTransactionAsync",
        "UseTransaction",
        "UseTransactionAsync");

    private static readonly ImmutableHashSet<string> TransactionEnds = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Commit",
        "CommitAsync",
        "Rollback",
        "RollbackAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.TransactionOwner);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var handlers = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);
            var suspects = new ConcurrentBag<(INamedTypeSymbol Type, Location Location, string Call)>();

            start.RegisterOperationAction(
                operation =>
                {
                    var invocation = (IInvocationOperation)operation.Operation;
                    if (MediatorRegistrations.TryRead(invocation, out var handler, out _))
                    {
                        handlers.TryAdd(handler, 0);
                        return;
                    }

                    Collect(invocation, OwnsTransaction(invocation, operation.Compilation), suspects);
                },
                OperationKind.Invocation);

            start.RegisterOperationAction(
                operation =>
                {
                    var creation = (IObjectCreationOperation)operation.Operation;
                    if (creation.Type is INamedTypeSymbol { Name: "TransactionScope" } type
                        && DuneSymbols.NamespaceOf(type).Equals("System.Transactions", StringComparison.Ordinal))
                    {
                        Collect(creation, "new TransactionScope", suspects);
                    }
                },
                OperationKind.ObjectCreation);

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var suspect in suspects)
                {
                    if (handlers.ContainsKey(suspect.Type) || handlers.ContainsKey(suspect.Type.OriginalDefinition))
                    {
                        end.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.TransactionOwner, suspect.Location, suspect.Type.Name, suspect.Call));
                    }
                }
            });
        });
    }

    private static void Collect(IOperation operation, string? call, ConcurrentBag<(INamedTypeSymbol, Location, string)> suspects)
    {
        if (call is null)
        {
            return;
        }

        var path = DunePaths.Normalize(operation.Syntax.SyntaxTree.FilePath);
        if (DunePaths.IsTestsFile(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || Array.IndexOf(OwnedFeatures, feature) < 0
            || MediatorRegistrations.EnclosingType(operation) is not { } type)
        {
            return;
        }

        suspects.Add((type, operation.Syntax.GetLocation(), call));
    }

    private static string? OwnsTransaction(IInvocationOperation invocation, Compilation compilation)
    {
        var method = invocation.TargetMethod;
        var owner = method.ContainingType;
        if (method.Name.Equals("Begin", StringComparison.Ordinal)
            && owner.Name.Equals("CrossContextTransaction", StringComparison.Ordinal)
            && DuneSymbols.NamespaceOf(owner).Equals("TemplateApp.Infrastructure.Persistence", StringComparison.Ordinal))
        {
            return "CrossContextTransaction.Begin";
        }

        if (FacadeTransactions.Contains(method.Name) && IsEf(method, "DatabaseFacade", "RelationalDatabaseFacadeExtensions"))
        {
            return method.Name;
        }

        if (TransactionEnds.Contains(method.Name) && IsEf(method, "IDbContextTransaction", "IDbContextTransaction"))
        {
            return method.Name;
        }

        if ((method.Name.Equals("SaveChanges", StringComparison.Ordinal) || method.Name.Equals("SaveChangesAsync", StringComparison.Ordinal))
            && owner is { } context
            && (DuneSymbols.DerivesFromDbContext(context, compilation) || IsEf(method, "DbContext", "DbContext"))
            && !TranslatesConstraint(invocation, compilation))
        {
            return method.Name;
        }

        return null;
    }

    private static bool IsEf(IMethodSymbol method, string type, string extensions)
    {
        var owner = method.ReducedFrom?.ContainingType ?? method.ContainingType;
        var receiver = method.ReducedFrom is not null ? method.ReducedFrom.Parameters[0].Type : owner;
        return DuneSymbols.NamespaceOf(owner).StartsWith(EfNamespace, StringComparison.Ordinal)
            && (owner.Name.Equals(type, StringComparison.Ordinal)
                || owner.Name.Equals(extensions, StringComparison.Ordinal)
                || receiver.Name.Equals(type, StringComparison.Ordinal));
    }

    private static bool TranslatesConstraint(IInvocationOperation invocation, Compilation compilation)
    {
        if (invocation.Parent is not IAwaitOperation)
        {
            return false;
        }

        var dbUpdate = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbUpdateException");
        for (var node = invocation.Syntax.Parent; node is not null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }

            if (node is BlockSyntax block && block.Parent is TryStatementSyntax tryStatement && tryStatement.Block == block)
            {
                var model = invocation.SemanticModel!;
                return tryStatement.Catches.Any(clause =>
                    clause.Declaration is { } declaration
                    && model.GetTypeInfo(declaration.Type).Type is INamedTypeSymbol caught
                    && DerivesFrom(caught, dbUpdate));
            }
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol? root)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, root))
            {
                return true;
            }
        }

        return false;
    }
}
