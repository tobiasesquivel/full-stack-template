using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MediatorEndpointAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] MediatorFeatures = { "Tasks", "Projects", "Users" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.MediatorEndpoint);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSemanticModelAction(Analyze);
    }

    private static void Analyze(SemanticModelAnalysisContext context)
    {
        var model = context.SemanticModel;
        var path = DunePaths.Normalize(model.SyntaxTree.FilePath);
        if (!DunePaths.IsSliceEndpoint(path)
            || DunePaths.IsTestsFile(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || Array.IndexOf(MediatorFeatures, feature) < 0)
        {
            return;
        }

        var root = model.SyntaxTree.GetRoot(context.CancellationToken);
        var folder = path.Substring(0, path.LastIndexOf('/') + 1);
        var dispatches = new List<IInvocationOperation>();
        foreach (var syntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetOperation(syntax, context.CancellationToken) is IInvocationOperation invocation
                && BusBoundaryAnalyzer.IsMediatorDispatch(invocation.TargetMethod))
            {
                dispatches.Add(invocation);
            }
        }

        if (dispatches.Count == 0)
        {
            var endpoint = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
            Report(context, endpoint?.Identifier.GetLocation() ?? root.GetLocation(), endpoint?.Identifier.Text ?? path, "no despacha su comando con IMessageBus.InvokeAsync<T>");
        }

        foreach (var extra in dispatches.Skip(1))
        {
            Report(context, extra.Syntax.GetLocation(), extra.TargetMethod.Name, "despacha más de una vez");
        }

        foreach (var dispatch in dispatches.Take(1))
        {
            var message = MessageType(dispatch);
            if (message is null || !DeclaredIn(message, folder))
            {
                Report(context, dispatch.Syntax.GetLocation(), message?.Name ?? dispatch.TargetMethod.Name, "despacha un mensaje que no está declarado en la carpeta de su slice");
            }
        }

        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            var type = (model.GetDeclaredSymbol(parameter, context.CancellationToken) as IParameterSymbol)?.Type;
            if (IsHandler(type, feature))
            {
                Report(context, parameter.GetLocation(), type!.Name, "recibe el handler en vez de despacharle el comando");
            }
        }

        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var type = model.GetTypeInfo(creation, context.CancellationToken).Type;
            if (IsHandler(type, feature))
            {
                Report(context, creation.GetLocation(), type!.Name, "construye el handler en vez de despacharle el comando");
            }
        }
    }

    private static ITypeSymbol? MessageType(IInvocationOperation dispatch)
    {
        var value = dispatch.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value?.Type;
    }

    private static bool DeclaredIn(ITypeSymbol type, string folder) =>
        type.DeclaringSyntaxReferences.Any(reference =>
        {
            var declared = DunePaths.Normalize(reference.SyntaxTree.FilePath);
            return declared.StartsWith(folder, StringComparison.Ordinal) && declared.IndexOf('/', folder.Length) < 0;
        });

    private static bool IsHandler(ITypeSymbol? type, string feature) =>
        type is INamedTypeSymbol named
        && DuneSymbols.NamespaceOf(named).StartsWith("TemplateApp.Features." + feature, StringComparison.Ordinal)
        && named.GetMembers("Handle").OfType<IMethodSymbol>().Any();

    private static void Report(SemanticModelAnalysisContext context, Location location, string subject, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.MediatorEndpoint, location, subject, reason));
}
