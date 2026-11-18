using System;
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
    private const string HttpNamespace = "System.Net.Http";
    private const string TransactionType = "CrossContextTransaction";
    private const string TransactionNamespace = "TemplateApp.Infrastructure.Persistence";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.RemoteCallInTransaction);

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

        var remote = RemoteReceiver(invocation);
        if (remote is null || invocation.SemanticModel is not { } model || !InsideTransaction(invocation.Syntax, model))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.RemoteCallInTransaction,
            invocation.Syntax.GetLocation(),
            invocation.TargetMethod.Name,
            remote.Name));
    }

    private static ITypeSymbol? RemoteReceiver(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.IsExtensionMethod && method.Parameters.Length > 0 && IsHttpInvoker(method.Parameters[0].Type))
        {
            return method.Parameters[0].Type;
        }

        var receiver = invocation.Instance?.Type;
        if (receiver is null)
        {
            return null;
        }

        return IsHttpInvoker(receiver) || IsTypedClient(receiver) ? receiver : null;
    }

    private static bool IsHttpInvoker(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.Name.Equals("HttpMessageInvoker", StringComparison.Ordinal)
                && DuneSymbols.NamespaceOf(current).Equals(HttpNamespace, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypedClient(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Any(parameter => IsHttpInvoker(parameter.Type)));

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
