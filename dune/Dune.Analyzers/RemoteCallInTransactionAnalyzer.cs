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
public sealed class RemoteCallInTransactionAnalyzer : DiagnosticAnalyzer
{
    private const string TransactionType = "CrossContextTransaction";
    private const string TransactionNamespace = "TemplateApp.Infrastructure.Persistence.Transactions";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.RemoteCallInTransaction, DuneDiagnostics.RemoteCallInMediatorTransaction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var transactional = new ConcurrentDictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
            var remoteCalls = new ConcurrentBag<(INamedTypeSymbol Type, Location Location, string Call, string Remote)>();

            start.RegisterOperationAction(
                operation => AnalyzeInvocation(operation, transactional, remoteCalls),
                OperationKind.Invocation);

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var call in remoteCalls)
                {
                    if (transactional.TryGetValue(call.Type, out var transaction))
                    {
                        end.ReportDiagnostic(Diagnostic.Create(
                            DuneDiagnostics.RemoteCallInMediatorTransaction,
                            call.Location,
                            call.Call,
                            call.Remote,
                            call.Type.Name,
                            transaction));
                    }
                }
            });
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, string> transactional,
        ConcurrentBag<(INamedTypeSymbol, Location, string, string)> remoteCalls)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (MediatorRegistrations.TryRead(invocation, out var handler, out var transaction))
        {
            if (transaction is MediatorRegistrations.Atomic or MediatorRegistrations.Eager)
            {
                transactional.TryAdd(handler, transaction);
            }

            return;
        }

        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!DunePaths.IsBackendFile(path) || DunePaths.IsMigration(path) || DunePaths.IsTestsFile(path))
        {
            return;
        }

        var remote = RemoteReceiver(invocation);
        if (remote is null || invocation.SemanticModel is not { } model)
        {
            return;
        }

        if (InsideTransaction(invocation.Syntax, model))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.RemoteCallInTransaction,
                invocation.Syntax.GetLocation(),
                invocation.TargetMethod.Name,
                remote.Name));
            return;
        }

        if (MediatorRegistrations.EnclosingType(invocation) is { } type && !SymbolEqualityComparer.Default.Equals(type, remote))
        {
            remoteCalls.Add((type, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name, remote.Name));
        }
    }

    private static ITypeSymbol? RemoteReceiver(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.IsExtensionMethod && method.Parameters.Length > 0 && DuneSymbols.IsHttpInvoker(method.Parameters[0].Type))
        {
            return method.Parameters[0].Type;
        }

        var receiver = invocation.Instance?.Type;
        if (receiver is null)
        {
            return null;
        }

        return DuneSymbols.IsHttpInvoker(receiver) || DuneSymbols.IsTypedClient(receiver) ? receiver : null;
    }

    private static bool InsideTransaction(SyntaxNode call, SemanticModel model)
    {
        SyntaxNode child = call;
        for (var node = call.Parent; node is not null; child = node, node = node.Parent)
        {
            switch (node)
            {
                case UsingStatementSyntax usingStatement
                    when child == usingStatement.Statement
                        && (OpensTransaction(usingStatement.Declaration, model) || OpensTransaction(usingStatement.Expression, model)):
                    return true;
                case BlockSyntax block when OpenedBefore(block.Statements, child, model):
                    return true;
                case SwitchSectionSyntax section when OpenedBefore(section.Statements, child, model):
                    return true;
                case CompilationUnitSyntax unit:
                    return unit.Members.OfType<GlobalStatementSyntax>()
                        .TakeWhile(member => member != child)
                        .Any(member => IsTransactionDeclaration(member.Statement, model));
                case BaseMethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case AccessorDeclarationSyntax:
                case AnonymousFunctionExpressionSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool OpenedBefore(SyntaxList<StatementSyntax> statements, SyntaxNode child, SemanticModel model) =>
        statements.TakeWhile(statement => statement != child).Any(statement => IsTransactionDeclaration(statement, model));

    private static bool IsTransactionDeclaration(StatementSyntax statement, SemanticModel model) =>
        statement is LocalDeclarationStatementSyntax declaration
        && declaration.UsingKeyword != default
        && OpensTransaction(declaration.Declaration, model);

    private static bool OpensTransaction(SyntaxNode? node, SemanticModel model) =>
        node is not null
        && node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation =>
            model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
            && method.Name.Equals("Begin", StringComparison.Ordinal)
            && method.ContainingType.Name.Equals(TransactionType, StringComparison.Ordinal)
            && DuneSymbols.NamespaceOf(method.ContainingType).Equals(TransactionNamespace, StringComparison.Ordinal));
}
