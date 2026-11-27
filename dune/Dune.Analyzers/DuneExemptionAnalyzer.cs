using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuneExemptionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.ExemptionWithoutReason);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        foreach (var exemption in DuneExemptions.Read(type))
        {
            var location = exemption.Attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Locations[0];
            if (exemption.Check is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.ExemptionWithoutReason, location, type.Name, "nombra un chequeo que no está en DuneCheck"));
            }
            else if (string.IsNullOrWhiteSpace(exemption.Reason))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.ExemptionWithoutReason, location, type.Name, $"exime {exemption.Check} sin decir el motivo"));
            }
        }
    }
}
