using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    private static readonly (string Method, string[] Parameters)[] Registrations =
    {
        ("AddOutboxEvent", new[] { "name", "version" }),
        ("AddIntegrationEventPublication", new[] { "name", "version", "exchange" }),
        ("AddIntegrationEventHandler", new[] { "name", "version", "exchange" }),
        ("AddIntegrationCommand", new[] { "name", "version", "exchange" }),
        ("AddSagaMessage", new[] { "name", "version", "exchange" }),
        ("AddRetiredBinding", new[] { "exchange", "routingKey" }),
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.BusNameOutsideMessageNames);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var findings = new ConcurrentBag<Diagnostic>();
            var messageNamesFiles = 0;

            start.RegisterOperationAction(operation =>
            {
                if (operation.Operation is IInvocationOperation invocation)
                {
                    AnalyzeArguments(invocation.TargetMethod, invocation.Arguments, invocation.Syntax, findings);
                }
                else if (operation.Operation is IObjectCreationOperation { Constructor: { } constructor } creation)
                {
                    AnalyzeArguments(constructor, creation.Arguments, creation.Syntax, findings);
                }
            }, OperationKind.Invocation, OperationKind.ObjectCreation);

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

                if (messageNamesFiles == 0)
                {
                    return;
                }

                var known = KnownRegistrations(end.Compilation);
                foreach (var (method, parameters) in Registrations.Where(registration => !known.Contains(Signature(registration.Method, registration.Parameters))))
                {
                    end.ReportDiagnostic(Diagnostic.Create(
                        DuneDiagnostics.BusNameOutsideMessageNames,
                        Location.None,
                        method,
                        "no está en " + MessagingNamespace + " con los parámetros " + string.Join(", ", parameters) + ": la regla quedó ciega para ese registro"));
                }
            });
        });
    }

    private static void AnalyzeArguments(
        IMethodSymbol method,
        ImmutableArray<IArgumentOperation> arguments,
        SyntaxNode syntax,
        ConcurrentBag<Diagnostic> findings)
    {
        var path = DunePaths.Normalize(syntax.SyntaxTree.FilePath);
        if (!IsChecked(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || !IsBusMember(method))
        {
            return;
        }

        foreach (var argument in arguments.Where(argument => argument.Parameter is { } parameter && IsNameParameter(parameter)))
        {
            if (!IsMessageNameConstant(argument.Value, feature))
            {
                findings.Add(Diagnostic.Create(
                    DuneDiagnostics.BusNameOutsideMessageNames,
                    argument.Syntax.GetLocation(),
                    argument.Syntax.ToString(),
                    "en " + method.ContainingType.Name + "." + method.Name + " no es una constante de Features/" + feature + "/" + DunePaths.MessageNamesFolder + "/"));
            }
        }
    }

    private static bool AnalyzeMessageNames(INamedTypeSymbol type, ConcurrentBag<Diagnostic> findings)
    {
        var location = type.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        var path = DunePaths.Normalize(location?.SourceTree?.FilePath);
        if (location is null
            || !IsChecked(path)
            || !DunePaths.TryGetFeature(path, out var feature)
            || !DunePaths.IsMessageNamesFile(path, feature))
        {
            return false;
        }

        var onlyConstants = type.ContainingType is null
            && type.TypeKind == TypeKind.Class
            && type.IsStatic
            && type.GetMembers().Where(member => !member.IsImplicitlyDeclared).All(member => member is IFieldSymbol { IsConst: true });
        if (!onlyConstants)
        {
            findings.Add(Diagnostic.Create(
                DuneDiagnostics.BusNameOutsideMessageNames,
                location,
                type.Name,
                "vive en " + DunePaths.MessageNamesFolder + "/ y no es una clase estática de primer nivel solo con constantes"));
        }

        return true;
    }

    private static bool IsMessageNameConstant(IOperation value, string feature)
    {
        while (value is IConversionOperation { IsImplicit: true } conversion)
        {
            value = conversion.Operand;
        }

        return value is IFieldReferenceOperation { Field: { IsConst: true } field }
            && field.DeclaringSyntaxReferences.Length > 0
            && field.DeclaringSyntaxReferences.All(reference =>
            {
                var declared = DunePaths.Normalize(reference.SyntaxTree.FilePath);
                return DunePaths.TryGetFeature(declared, out var owner)
                    && owner.Equals(feature, StringComparison.Ordinal)
                    && DunePaths.IsMessageNamesFile(declared, owner);
            });
    }

    private static HashSet<string> KnownRegistrations(Compilation compilation)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        var root = compilation.GlobalNamespace;
        foreach (var part in MessagingNamespace.Split('.'))
        {
            root = root?.GetNamespaceMembers().FirstOrDefault(member => member.Name.Equals(part, StringComparison.Ordinal));
        }

        if (root is not null)
        {
            CollectRegistrations(root, known);
        }

        return known;
    }

    private static void CollectRegistrations(INamespaceOrTypeSymbol container, HashSet<string> known)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol nested)
            {
                CollectRegistrations(nested, known);
            }
            else if (member is IMethodSymbol method)
            {
                foreach (var (registration, parameters) in Registrations)
                {
                    if (method.Name.Equals(registration, StringComparison.Ordinal)
                        && parameters.All(parameter => method.Parameters.Any(candidate => candidate.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase))))
                    {
                        known.Add(Signature(registration, parameters));
                    }
                }
            }
        }
    }

    private static string Signature(string method, string[] parameters) =>
        method + "(" + string.Join(",", parameters) + ")";

    private static bool IsBusMember(IMethodSymbol method) =>
        DuneSymbols.InNamespace(method.ContainingType, MessagingNamespace)
        && method.Parameters.Any(IsNameParameter);

    private static bool IsNameParameter(IParameterSymbol parameter) =>
        NameParameters.Any(name => name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));

    private static bool IsChecked(string path) =>
        DunePaths.IsBackendFile(path) && !DunePaths.IsMigration(path) && !DunePaths.IsTestsFile(path);
}
