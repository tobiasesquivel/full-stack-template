using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

    private static readonly OperationKind[] AllOperationKinds = Enum.GetValues(typeof(OperationKind))
        .Cast<OperationKind>()
        .Where(kind => kind != OperationKind.None)
        .Distinct()
        .ToArray();

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeName, SyntaxKind.IdentifierName, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterOperationAction(AnalyzeOperation, AllOperationKinds);
        context.RegisterSyntaxNodeAction(
            AnalyzeCompilerPattern,
            SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement,
            SyntaxKind.AwaitExpression,
            SyntaxKind.SimpleAssignmentExpression);
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

        Report(context.ReportDiagnostic, node, foreign);
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        var operation = context.Operation;
        if (!TryGetOwner(operation.Syntax.SyntaxTree, out var owner))
        {
            return;
        }

        var foreign = FindForeignInterior(operation.Type, owner)
            ?? FirstForeign(ExtraTypes(operation), owner)
            ?? FirstForeignMember(UsedMembers(operation), owner);

        Report(context.ReportDiagnostic, operation.Syntax, foreign);
    }

    private static void AnalyzeCompilerPattern(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetOwner(context.Node.SyntaxTree, out var owner))
        {
            return;
        }

        var model = context.SemanticModel;
        var members = new List<ISymbol?>();
        var types = new List<ITypeSymbol?>();
        switch (context.Node)
        {
            case CommonForEachStatementSyntax loop:
                var loopInfo = model.GetForEachStatementInfo(loop);
                members.Add(loopInfo.GetEnumeratorMethod);
                members.Add(loopInfo.MoveNextMethod);
                members.Add(loopInfo.CurrentProperty);
                members.Add(loopInfo.DisposeMethod);
                types.Add(loopInfo.ElementType);
                if (loop is ForEachVariableStatementSyntax deconstructed)
                {
                    AddDeconstructMethods(model.GetDeconstructionInfo(deconstructed), members);
                }

                break;
            case AwaitExpressionSyntax awaited:
                var awaitInfo = model.GetAwaitExpressionInfo(awaited);
                members.Add(awaitInfo.GetAwaiterMethod);
                members.Add(awaitInfo.GetResultMethod);
                members.Add(awaitInfo.IsCompletedProperty);
                break;
            case AssignmentExpressionSyntax { Left: TupleExpressionSyntax or DeclarationExpressionSyntax } assignment:
                AddDeconstructMethods(model.GetDeconstructionInfo(assignment), members);
                break;
        }

        Report(context.ReportDiagnostic, context.Node, FirstForeign(types, owner) ?? FirstForeignMember(members, owner));
    }

    private static void AddDeconstructMethods(DeconstructionInfo info, List<ISymbol?> methods)
    {
        methods.Add(info.Method);
        foreach (var nested in info.Nested)
        {
            AddDeconstructMethods(nested, methods);
        }
    }

    private static IEnumerable<ISymbol?> UsedMembers(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => new ISymbol?[] { invocation.TargetMethod },
        IMemberReferenceOperation reference => new ISymbol?[] { reference.Member },
        IObjectCreationOperation creation => new ISymbol?[] { creation.Constructor },
        IBinaryOperation binary => new ISymbol?[] { binary.OperatorMethod },
        IUnaryOperation unary => new ISymbol?[] { unary.OperatorMethod },
        ICompoundAssignmentOperation compound => new ISymbol?[] { compound.OperatorMethod },
        IIncrementOrDecrementOperation step => new ISymbol?[] { step.OperatorMethod },
        IConversionOperation conversion => new ISymbol?[] { conversion.OperatorMethod },
        ICollectionExpressionOperation collection => new ISymbol?[] { collection.ConstructMethod },
        IRecursivePatternOperation pattern => new ISymbol?[] { pattern.DeconstructSymbol },
        IListPatternOperation list => new ISymbol?[] { list.LengthSymbol, list.IndexerSymbol },
        ISlicePatternOperation slice => new ISymbol?[] { slice.SliceSymbol },
        IImplicitIndexerReferenceOperation indexer => new ISymbol?[] { indexer.LengthSymbol, indexer.IndexerSymbol },
        _ => Array.Empty<ISymbol?>(),
    };

    private static IEnumerable<ITypeSymbol?> ExtraTypes(IOperation operation) => operation switch
    {
        IPatternOperation pattern => new[] { pattern.InputType, pattern.NarrowedType },
        ISpreadOperation spread => new[] { spread.ElementType },
        _ => Array.Empty<ITypeSymbol?>(),
    };

    private static IEnumerable<ITypeSymbol?> SignatureTypes(ISymbol? member) => member switch
    {
        IMethodSymbol method => method.Parameters.Select(parameter => (ITypeSymbol?)parameter.Type).Prepend(method.ReturnType),
        IPropertySymbol property => property.Parameters.Select(parameter => (ITypeSymbol?)parameter.Type).Prepend(property.Type),
        IFieldSymbol field => new ITypeSymbol?[] { field.Type },
        IEventSymbol @event => new ITypeSymbol?[] { @event.Type },
        _ => Array.Empty<ITypeSymbol?>(),
    };

    private static ITypeSymbol? FirstForeignMember(IEnumerable<ISymbol?> members, string owner) =>
        members
            .Select(member => FindForeignInterior(DeclaringType(member), owner) ?? FirstForeign(SignatureTypes(member), owner))
            .FirstOrDefault(found => found is not null);

    private static ITypeSymbol? FirstForeign(IEnumerable<ITypeSymbol?> types, string owner) =>
        types
            .Select(type => FindForeignInterior(type, owner))
            .FirstOrDefault(found => found is not null);

    private static void Report(Action<Diagnostic> report, SyntaxNode node, ITypeSymbol? foreign)
    {
        if (foreign is not null)
        {
            report(Diagnostic.Create(DuneDiagnostics.CrossFeature, node.GetLocation(), foreign.ToDisplayString()));
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
            case IPointerTypeSymbol pointer:
                return FindForeignInterior(pointer.PointedAtType, owner);
            case INamedTypeSymbol named:
                if (IsForeignInterior(DuneSymbols.NamespaceOf(named.OriginalDefinition), owner))
                {
                    return named;
                }

                if (named.ContainingType is not null
                    && FindForeignInterior(named.ContainingType, owner) is { } container)
                {
                    return container;
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
