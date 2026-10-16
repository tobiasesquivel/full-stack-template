using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainDependencyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.DomainDependency);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var reported = new ConcurrentDictionary<string, byte>(System.StringComparer.Ordinal);
            start.RegisterSymbolAction(
                context => Analyze(context, reported),
                SymbolKind.NamedType);
            start.RegisterOperationAction(
                context => AnalyzeOperation(context, reported),
                OperationKind.ObjectCreation,
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.EventReference,
                OperationKind.TypeOf,
                OperationKind.Conversion);
            start.RegisterSyntaxNodeAction(
                context => AnalyzeTypeSyntax(context, reported),
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName,
                SyntaxKind.QualifiedName,
                SyntaxKind.AliasQualifiedName,
                SyntaxKind.NullableType,
                SyntaxKind.ArrayType,
                SyntaxKind.PointerType,
                SyntaxKind.FunctionPointerType,
                SyntaxKind.TupleType,
                SyntaxKind.TypeOfExpression,
                SyntaxKind.CastExpression);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ConcurrentDictionary<string, byte> reported)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Enum or TypeKind.Interface))
        {
            return;
        }

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var path = DunePaths.Normalize(reference.SyntaxTree.FilePath);
            if (!DunePaths.IsBackendFile(path)
                || DunePaths.IsMigration(path)
                || !DunePaths.IsDomainFile(path))
            {
                continue;
            }

            CheckBaseTypes(context, reported, type);
            CheckMembers(context, reported, type);
            return;
        }
    }

    private static void AnalyzeOperation(OperationAnalysisContext context, ConcurrentDictionary<string, byte> reported)
    {
        var path = DunePaths.Normalize(context.Operation.Syntax.SyntaxTree.FilePath);
        if (!IsDomainExecutable(path))
        {
            return;
        }

        var owner = ContainingDomainType(context.Operation);
        if (owner is null)
        {
            return;
        }

        if (context.Operation is IObjectCreationOperation creation)
        {
            CheckType(context, reported, owner, creation.Type, creation.Syntax.GetLocation());
            return;
        }

        if (context.Operation is IInvocationOperation invocation)
        {
            CheckSymbol(context, reported, owner, invocation.TargetMethod.ContainingType, invocation.Syntax.GetLocation());
            return;
        }

        if (context.Operation is IMemberReferenceOperation member)
        {
            CheckSymbol(context, reported, owner, member.Member.ContainingType, member.Syntax.GetLocation());
            return;
        }

        if (context.Operation is ITypeOfOperation typeOf)
        {
            CheckType(context, reported, owner, typeOf.TypeOperand, typeOf.Syntax.GetLocation());
            return;
        }

        if (context.Operation is IConversionOperation conversion)
        {
            CheckType(context, reported, owner, conversion.Type, conversion.Syntax.GetLocation());
        }
    }

    private static void AnalyzeTypeSyntax(SyntaxNodeAnalysisContext context, ConcurrentDictionary<string, byte> reported)
    {
        if (!IsInExecutableRegion(context.Node))
        {
            return;
        }

        var path = DunePaths.Normalize(context.Node.SyntaxTree.FilePath);
        if (!IsDomainExecutable(path))
        {
            return;
        }

        var owner = ContainingDomainType(context.ContainingSymbol);
        if (owner is null)
        {
            return;
        }

        if (context.Node is TypeSyntax typeSyntax)
        {
            if (context.SemanticModel.GetSymbolInfo(typeSyntax, context.CancellationToken).Symbol is INamedTypeSymbol named)
            {
                CheckSymbol(context, reported, owner, named, typeSyntax.GetLocation());
            }

            return;
        }

        var expression = (ExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is INamedTypeSymbol direct)
        {
            CheckSymbol(context, reported, owner, direct, expression.GetLocation());
        }
    }

    private static bool IsInExecutableRegion(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is StatementSyntax
                || current is AnonymousFunctionExpressionSyntax
                || current is LocalFunctionStatementSyntax
                || current is ArrowExpressionClauseSyntax
                || current is EqualsValueClauseSyntax)
            {
                return true;
            }

            if (current is BaseTypeDeclarationSyntax
                or BaseFieldDeclarationSyntax
                or BaseMethodDeclarationSyntax
                or BasePropertyDeclarationSyntax
                or DelegateDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static void CheckBaseTypes(SymbolAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol type)
    {
        if (type.BaseType is not null
            && !type.BaseType.SpecialType.Equals(SpecialType.System_Object)
            && !IsAllowed(type.BaseType, context.Compilation, type))
        {
            Report(context, reported, type, type.BaseType, LocationOf(type));
        }

        foreach (var contract in type.Interfaces)
        {
            if (!IsAllowed(contract, context.Compilation, type))
            {
                Report(context, reported, type, contract, LocationOf(type));
            }
        }
    }

    private static void CheckMembers(SymbolAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            var location = member.Locations.Length > 0 ? member.Locations[0] : LocationOf(type);

            ITypeSymbol? memberType = member switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                IMethodSymbol method => method.ReturnType,
                IEventSymbol @event => @event.Type,
                _ => null,
            };

            if (memberType is not null && !IsAllowed(memberType, context.Compilation, type))
            {
                Report(context, reported, type, memberType, location);
            }

            if (member is IMethodSymbol methodSymbol)
            {
                foreach (var parameter in methodSymbol.Parameters)
                {
                    if (!IsAllowed(parameter.Type, context.Compilation, type))
                    {
                        var parameterLocation = parameter.Locations.Length > 0 ? parameter.Locations[0] : location;
                        Report(context, reported, type, parameter.Type, parameterLocation);
                    }
                }
            }
        }
    }

    private static void CheckType(OperationAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, ITypeSymbol? type, Location location)
    {
        if (type is null || IsAllowed(type, context.Compilation, owner))
        {
            return;
        }

        Report(context, reported, owner, type, location);
    }

    private static void CheckSymbol(SyntaxNodeAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, INamedTypeSymbol? used, Location location)
    {
        if (used is null || IsAllowed(used, context.Compilation, owner))
        {
            return;
        }

        Report(context, reported, owner, used, location);
    }

    private static void CheckSymbol(OperationAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, INamedTypeSymbol? used, Location location)
    {
        if (used is null || IsAllowed(used, context.Compilation, owner))
        {
            return;
        }

        Report(context, reported, owner, used, location);
    }

    private static bool IsDomainExecutable(string normalizedPath) =>
        DunePaths.IsBackendFile(normalizedPath)
        && !DunePaths.IsMigration(normalizedPath)
        && DunePaths.IsDomainFile(normalizedPath);

    private static INamedTypeSymbol? ContainingDomainType(IOperation operation)
    {
        var syntax = operation.Syntax;
        while (syntax is not null)
        {
            if (syntax is BaseTypeDeclarationSyntax declaration)
            {
                var model = operation.SemanticModel;
                if (model is null)
                {
                    return null;
                }

                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol named
                    && named.TypeKind is (TypeKind.Class or TypeKind.Struct or TypeKind.Enum or TypeKind.Interface))
                {
                    return named;
                }

                return null;
            }

            syntax = syntax.Parent;
        }

        return null;
    }

    private static INamedTypeSymbol? ContainingDomainType(ISymbol? symbol)
    {
        while (symbol is not null && symbol is not INamedTypeSymbol)
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol as INamedTypeSymbol;
    }

    private static bool IsAllowed(ITypeSymbol type, Compilation compilation, INamedTypeSymbol owner)
    {
        if (type is ITypeParameterSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsAllowed(array.ElementType, compilation, owner);
        }

        if (type is IPointerTypeSymbol pointer)
        {
            return IsAllowed(pointer.PointedAtType, compilation, owner);
        }

        var unwrapped = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        if (unwrapped is not INamedTypeSymbol named)
        {
            return true;
        }

        if (named.OriginalDefinition.SpecialType is not SpecialType.None)
        {
            return true;
        }

        if (DuneSymbols.InNamespace(named.OriginalDefinition, "System"))
        {
            return IsAllowedGenericArguments(named, compilation, owner);
        }

        if (DuneSymbols.InNamespace(named.OriginalDefinition, "TemplateApp.Shared.Domain"))
        {
            return IsAllowedGenericArguments(named, compilation, owner);
        }

        var ownerFeature = OwnerFeature(owner);
        var usedNs = DuneSymbols.NamespaceOf(named.OriginalDefinition);
        if (!string.IsNullOrEmpty(ownerFeature)
            && usedNs.StartsWith("TemplateApp.Features." + ownerFeature + ".Domain", System.StringComparison.Ordinal)
            && IsAllowedGenericArguments(named, compilation, owner))
        {
            return true;
        }

        if (named.TypeKind is TypeKind.Enum
            || named.TypeKind is TypeKind.Delegate
            || named.TypeKind is TypeKind.TypeParameter)
        {
            return IsAllowedGenericArguments(named, compilation, owner);
        }

        return false;
    }

    private static bool IsAllowedGenericArguments(INamedTypeSymbol named, Compilation compilation, INamedTypeSymbol owner)
    {
        if (!named.IsGenericType)
        {
            return true;
        }

        foreach (var argument in named.TypeArguments)
        {
            if (!IsAllowed(argument, compilation, owner))
            {
                return false;
            }
        }

        return true;
    }

    private static string OwnerFeature(INamedTypeSymbol owner)
    {
        foreach (var reference in owner.DeclaringSyntaxReferences)
        {
            var path = DunePaths.Normalize(reference.SyntaxTree.FilePath);
            if (DunePaths.TryGetFeature(path, out var feature))
            {
                return feature;
            }
        }

        var ns = DuneSymbols.NamespaceOf(owner);
        const string prefix = "TemplateApp.Features.";
        if (ns.StartsWith(prefix, System.StringComparison.Ordinal))
        {
            var rest = ns.Substring(prefix.Length);
            var dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest.Substring(0, dot);
        }

        return string.Empty;
    }

    private static Location LocationOf(INamedTypeSymbol owner) =>
        owner.Locations.Length > 0 ? owner.Locations[0] : Location.None;

    private static string ReportKey(INamedTypeSymbol owner, ITypeSymbol used, Location location)
    {
        var span = location.SourceSpan;
        var path = location.SourceTree is null
            ? string.Empty
            : DunePaths.Normalize(location.SourceTree.FilePath);
        return owner.ToDisplayString() + "|" + used.ToDisplayString() + "|" + path + "|" + span.Start + "-" + span.End;
    }

    private static bool TryAdd(ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, ITypeSymbol used, Location location) =>
        reported.TryAdd(ReportKey(owner, used, location), 0);

    private static void Report(SymbolAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, ITypeSymbol used, Location location)
    {
        if (!TryAdd(reported, owner, used, location))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.DomainDependency,
            location,
            used.ToDisplayString()));
    }

    private static void Report(SyntaxNodeAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, ITypeSymbol used, Location location)
    {
        if (!TryAdd(reported, owner, used, location))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.DomainDependency,
            location,
            used.ToDisplayString()));
    }

    private static void Report(OperationAnalysisContext context, ConcurrentDictionary<string, byte> reported, INamedTypeSymbol owner, ITypeSymbol used, Location location)
    {
        if (!TryAdd(reported, owner, used, location))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.DomainDependency,
            location,
            used.ToDisplayString()));
    }
}
