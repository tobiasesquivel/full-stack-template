using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BusNameAnalyzer : DiagnosticAnalyzer
{
    private const string MessagingNamespace = "TemplateApp.Infrastructure.Messaging";
    private static readonly string[] NameParameters = { "name", "version", "exchange", "routingKey" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.BusNameOutsideMessageNames);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var findings = new ConcurrentBag<Diagnostic>();
            var registrations = 0;
            var messageNamesFiles = 0;

            start.RegisterOperationAction(operation =>
            {
                if (AnalyzeRegistration((IInvocationOperation)operation.Operation, findings))
                {
                    Interlocked.Increment(ref registrations);
                }
            }, OperationKind.Invocation);

            start.RegisterSymbolAction(symbol =>
            {
                if (AnalyzeMessageNames((INamedTypeSymbol)symbol.Symbol, findings))
                {
                    Interlocked.Increment(ref messageNamesFiles);
                }
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var finding in findings)
                {
                    end.ReportDiagnostic(finding);
                }

                if (messageNamesFiles > 0 && registrations == 0)
                {
                    end.ReportDiagnostic(Diagnostic.Create(
                        DuneDiagnostics.BusNameOutsideMessageNames,
                        Location.None,
                        MessagingNamespace,
                        "no tiene ningún registro del bus en las features aunque hay MessageNames/: si los registros se mudaron, la regla quedó ciega"));
                }
            });
        });
    }

    private static bool AnalyzeRegistration(IInvocationOperation invocation, ConcurrentBag<Diagnostic> findings)
    {
        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        var method = invocation.TargetMethod;
        if (!IsChecked(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || !DuneSymbols.InNamespace(method.ContainingType, MessagingNamespace)
            || !method.Parameters.Any(IsNameParameter))
        {
            return false;
        }

        foreach (var argument in invocation.Arguments.Where(argument => argument.Parameter is { } parameter && IsNameParameter(parameter)))
        {
            if (!IsMessageNameConstant(argument.Value, feature))
            {
                findings.Add(Diagnostic.Create(
                    DuneDiagnostics.BusNameOutsideMessageNames,
                    argument.Syntax.GetLocation(),
                    argument.Syntax.ToString(),
                    "en " + method.Name + " no es una constante de Features/" + feature + "/" + DunePaths.MessageNamesFolder + "/"));
            }
        }

        return true;
    }

    private static bool AnalyzeMessageNames(INamedTypeSymbol type, ConcurrentBag<Diagnostic> findings)
    {
        var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        var path = DunePaths.Normalize(location?.SourceTree?.FilePath);
        if (location is null
            || type.ContainingType is not null
            || !IsChecked(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || !DunePaths.IsMessageNamesFile(path, feature))
        {
            return false;
        }

        var onlyConstants = type.TypeKind == TypeKind.Class
            && type.IsStatic
            && type.GetMembers().Where(member => !member.IsImplicitlyDeclared).All(member => member is IFieldSymbol { IsConst: true });
        if (!onlyConstants)
        {
            findings.Add(Diagnostic.Create(
                DuneDiagnostics.BusNameOutsideMessageNames,
                location,
                type.Name,
                "vive en " + DunePaths.MessageNamesFolder + "/ y no es una clase estática solo con constantes"));
        }

        return true;
    }

    private static bool IsMessageNameConstant(IOperation value, string feature)
    {
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value is IFieldReferenceOperation { Field: { IsConst: true } field }
            && field.ContainingType.DeclaringSyntaxReferences.Any(reference =>
            {
                var declared = DunePaths.Normalize(reference.SyntaxTree.FilePath);
                return DunePaths.TryGetFeature(declared, out var owner)
                    && owner.Equals(feature, StringComparison.Ordinal)
                    && DunePaths.IsMessageNamesFile(declared, owner);
            });
    }

    private static bool IsNameParameter(IParameterSymbol parameter) =>
        Array.IndexOf(NameParameters, parameter.Name) >= 0;

    private static bool IsChecked(string path) =>
        DunePaths.IsBackendFile(path) && !DunePaths.IsMigration(path) && !DunePaths.IsTestsFile(path);
}
