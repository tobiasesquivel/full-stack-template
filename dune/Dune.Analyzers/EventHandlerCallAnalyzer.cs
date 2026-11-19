using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventHandlerCallAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerName = "IEventHandler";
    private const string MessagingNamespace = "TemplateApp.Infrastructure.Messaging";
    private const string RelayFile = "backend/Infrastructure/Messaging/OutboxRelay.cs";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.EventHandlerCalledDirectly);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var handler = IsHandler(invocation.Instance?.Type) ? invocation.Instance!.Type
            : IsHandler(invocation.TargetMethod.ContainingType) ? invocation.TargetMethod.ContainingType
            : null;
        if (handler is not null && IsChecked(invocation.Syntax))
        {
            Report(context, invocation.Syntax, invocation.TargetMethod.Name, handler);
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (IsHandler(creation.Type) && IsChecked(creation.Syntax))
        {
            Report(context, creation.Syntax, "new", creation.Type!);
        }
    }

    private static void Report(OperationAnalysisContext context, SyntaxNode syntax, string member, ITypeSymbol handler) =>
        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.EventHandlerCalledDirectly,
            syntax.GetLocation(),
            member,
            handler.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

    private static bool IsChecked(SyntaxNode syntax)
    {
        var path = DunePaths.Normalize(syntax.SyntaxTree.FilePath);
        return DunePaths.IsBackendFile(path)
            && !DunePaths.IsMigration(path)
            && !DunePaths.IsTestsFile(path)
            && !path.EndsWith(RelayFile, StringComparison.Ordinal);
    }

    private static bool IsHandler(ITypeSymbol? type) =>
        type switch
        {
            null => false,
            ITypeParameterSymbol parameter => parameter.ConstraintTypes.Any(IsHandler),
            _ => IsHandlerInterface(type) || type.AllInterfaces.Any(IsHandlerInterface),
        };

    private static bool IsHandlerInterface(ITypeSymbol type) =>
        type is INamedTypeSymbol { Arity: 1 } named
        && named.Name.Equals(HandlerName, StringComparison.Ordinal)
        && DuneSymbols.NamespaceOf(named.OriginalDefinition).Equals(MessagingNamespace, StringComparison.Ordinal);
}
