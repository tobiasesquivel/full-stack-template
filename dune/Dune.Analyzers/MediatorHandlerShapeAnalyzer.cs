using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MediatorHandlerShapeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.MediatorHandlerShape);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.ContainingType is not { } container
            || type.DeclaredAccessibility != Accessibility.Public
            || !type.GetMembers("Handle").OfType<IMethodSymbol>().Any())
        {
            return;
        }

        foreach (var location in type.Locations.Where(location => location.IsInSource))
        {
            var path = DunePaths.Normalize(location.SourceTree?.FilePath);
            if (DunePaths.IsTestsFile(path)
                || !DunePaths.TryGetSlice(path, out _))
            {
                continue;
            }

            var message = container.Name;
            if (!container.IsRecord || !(message.EndsWith("Command", StringComparison.Ordinal) || message.EndsWith("Query", StringComparison.Ordinal)))
            {
                Report(context, location, type.Name, $"está anidado en '{message}', que no es un record <Slice>Command ni <Slice>Query");
                continue;
            }

            if (!type.Name.Equals("Handler", StringComparison.Ordinal))
            {
                Report(context, location, type.Name, $"se llama '{type.Name}' y no 'Handler'");
            }

            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!fileName.Equals(message + "Handler", StringComparison.Ordinal))
            {
                Report(context, location, type.Name, $"vive en '{fileName}.cs' y no en '{message}Handler.cs'");
            }

            var messageFile = path.Substring(0, path.LastIndexOf('/') + 1) + message + ".cs";
            if (!container.DeclaringSyntaxReferences.Any(reference => DunePaths.Normalize(reference.SyntaxTree.FilePath).Equals(messageFile, StringComparison.Ordinal)))
            {
                Report(context, location, type.Name, $"está en un mensaje que no se declara en '{message}.cs', separado del handler");
            }
        }
    }

    private static void Report(SymbolAnalysisContext context, Location location, string subject, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.MediatorHandlerShape, location, subject, reason));
}
