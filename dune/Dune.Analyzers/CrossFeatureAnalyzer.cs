using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossFeatureAnalyzer : DiagnosticAnalyzer
{
    private const string FeaturesNamespace = "TemplateApp.Features.";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.CrossFeature);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterOperationAction(
            AnalyzeOperation,
            OperationKind.Invocation,
            OperationKind.MethodReference,
            OperationKind.PropertyReference,
            OperationKind.FieldReference,
            OperationKind.EventReference,
            OperationKind.ObjectCreation,
            OperationKind.DelegateCreation,
            OperationKind.Binary,
            OperationKind.Unary,
            OperationKind.Conversion,
            OperationKind.DefaultValue);
        context.RegisterSyntaxNodeAction(
            AnalyzePatternMember,
            SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement,
            SyntaxKind.AwaitExpression,
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.RecursivePattern);
    }

    private static void AnalyzePatternMember(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetOwner(context.Node.SyntaxTree, out var owner))
        {
            return;
        }

        var model = context.SemanticModel;
        var methods = context.Node switch
        {
            ForEachVariableStatementSyntax loop => new ISymbol?[] { model.GetForEachStatementInfo(loop).GetEnumeratorMethod, model.GetDeconstructionInfo(loop).Method },
            CommonForEachStatementSyntax loop => new ISymbol?[] { model.GetForEachStatementInfo(loop).GetEnumeratorMethod },
            AwaitExpressionSyntax awaited => new ISymbol?[] { model.GetAwaitExpressionInfo(awaited).GetAwaiterMethod },
            AssignmentExpressionSyntax assignment => new ISymbol?[] { model.GetDeconstructionInfo(assignment).Method },
            _ => new ISymbol?[] { (model.GetOperation(context.Node, context.CancellationToken) as IRecursivePatternOperation)?.DeconstructSymbol },
        };

        foreach (var method in methods)
        {
            var foreign = FindForeignInterior(DeclaringType(method), owner);
            if (foreign is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.CrossFeature, context.Node.GetLocation(), foreign.ToDisplayString()));
                return;
            }
        }
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        if (!TryGetOwner(node.SyntaxTree, out var owner))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
        var foreign = symbol is ITypeSymbol named
            ? FindForeignInterior(named, owner)
            : FindForeignInterior(context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type, owner);

        if (foreign is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.CrossFeature, node.GetLocation(), foreign.ToDisplayString()));
        }
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        var operation = context.Operation;
        if (!TryGetOwner(operation.Syntax.SyntaxTree, out var owner))
        {
            return;
        }

        ISymbol? member = operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMemberReferenceOperation reference => reference.Member,
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IConversionOperation conversion => conversion.OperatorMethod,
            _ => null,
        };

        var foreign = FindForeignInterior(DeclaringType(member), owner)
            ?? FindForeignInterior(operation.Type, owner);

        if (foreign is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.CrossFeature, operation.Syntax.GetLocation(), foreign.ToDisplayString()));
        }
    }

    private static INamedTypeSymbol? DeclaringType(ISymbol? member)
    {
        var type = member?.ContainingType;
        return type is { IsExtension: true } ? type.ContainingType : type;
    }

    private static bool TryGetOwner(SyntaxTree tree, out string owner)
    {
        owner = string.Empty;
        var path = DunePaths.Normalize(tree.FilePath);
        return DunePaths.IsBackendFile(path)
            && !DunePaths.IsMigration(path)
            && DunePaths.TryGetFeature(path, out owner);
    }

    private static ITypeSymbol? FindForeignInterior(ITypeSymbol? type, string owner)
    {
        switch (type)
        {
            case null:
                return null;
            case IArrayTypeSymbol array:
                return FindForeignInterior(array.ElementType, owner);
            case INamedTypeSymbol named:
                if (IsForeignInterior(DuneSymbols.NamespaceOf(named.OriginalDefinition), owner))
                {
                    return named;
                }

                foreach (var argument in named.TypeArguments)
                {
                    var found = FindForeignInterior(argument, owner);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static bool IsForeignInterior(string ns, string owner)
    {
        if (!ns.StartsWith(FeaturesNamespace, System.StringComparison.Ordinal))
        {
            return false;
        }

        var rest = ns.Substring(FeaturesNamespace.Length);
        var dot = rest.IndexOf('.');
        var feature = dot < 0 ? rest : rest.Substring(0, dot);
        var tail = dot < 0 ? string.Empty : rest.Substring(dot + 1);
        if (feature.Length == 0 || feature.Equals(owner, System.StringComparison.Ordinal))
        {
            return false;
        }

        return !tail.Equals("Contracts", System.StringComparison.Ordinal);
    }
}
