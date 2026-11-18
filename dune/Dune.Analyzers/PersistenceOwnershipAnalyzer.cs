using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PersistenceOwnershipAnalyzer : DiagnosticAnalyzer
{
    private const string InfrastructurePersistence = "backend/Infrastructure/Persistence/";
    private const string TransactionHelper = InfrastructurePersistence + "CrossContextTransaction.cs";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DuneDiagnostics.DbContextOutsidePersistence,
            DuneDiagnostics.SecondDbContextInFeature,
            DuneDiagnostics.ExcludeFromMigrationsOutsideInfrastructure,
            DuneDiagnostics.TransactionScopeOutsideHelper,
            DuneDiagnostics.ConfigurationOfAnotherOwner);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var contexts = new ConcurrentDictionary<string, ConcurrentDictionary<string, Location>>(StringComparer.Ordinal);

            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, contexts), SymbolKind.NamedType);
            start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
            start.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (var feature in contexts.Where(entry => entry.Value.Count > 1))
                {
                    var names = string.Join(", ", feature.Value.Keys.OrderBy(name => name, StringComparer.Ordinal));
                    foreach (var location in feature.Value.Values)
                    {
                        end.ReportDiagnostic(Diagnostic.Create(
                            DuneDiagnostics.SecondDbContextInFeature,
                            location,
                            feature.Key,
                            feature.Value.Count,
                            names));
                    }
                }
            });
        });
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context,
        ConcurrentDictionary<string, ConcurrentDictionary<string, Location>> contexts)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || !DuneSymbols.DerivesFromDbContext(type, context.Compilation))
        {
            return;
        }

        foreach (var location in type.Locations.Where(candidate => candidate.IsInSource))
        {
            var path = DunePaths.Normalize(location.SourceTree?.FilePath);
            if (!IsChecked(path))
            {
                continue;
            }

            if (IsFeaturePersistence(path, out var feature))
            {
                contexts.GetOrAdd(feature, _ => new ConcurrentDictionary<string, Location>(StringComparer.Ordinal))
                    .TryAdd(type.ToDisplayString(), location);
            }
            else if (!IsInfrastructurePersistence(path))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.DbContextOutsidePersistence, location, type.Name));
            }
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!DuneSymbols.InNamespace(method.ContainingType.OriginalDefinition, "Microsoft.EntityFrameworkCore"))
        {
            return;
        }

        var path = DunePaths.Normalize(invocation.Syntax.SyntaxTree.FilePath);
        if (!IsChecked(path))
        {
            return;
        }

        if (method.Name.Equals("ApplyConfigurationsFromAssembly", StringComparison.Ordinal)
            || (method.Name.Equals("ApplyConfiguration", StringComparison.Ordinal) && !OwnsConfiguration(path, invocation)))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.ConfigurationOfAnotherOwner, invocation.Syntax.GetLocation()));
            return;
        }

        if (method.Name.Equals("ExcludeFromMigrations", StringComparison.Ordinal) && !IsInfrastructurePersistence(path))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuneDiagnostics.ExcludeFromMigrationsOutsideInfrastructure,
                invocation.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is not INamedTypeSymbol type
            || !type.Name.Equals("TransactionScope", StringComparison.Ordinal)
            || !DuneSymbols.NamespaceOf(type).Equals("System.Transactions", StringComparison.Ordinal))
        {
            return;
        }

        var path = DunePaths.Normalize(creation.Syntax.SyntaxTree.FilePath);
        if (IsChecked(path) && !path.EndsWith(TransactionHelper, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.TransactionScopeOutsideHelper, creation.Syntax.GetLocation()));
        }
    }

    private static bool OwnsConfiguration(string path, IInvocationOperation invocation)
    {
        var configuration = invocation.Arguments.FirstOrDefault()?.Value is IConversionOperation conversion
            ? conversion.Operand.Type
            : invocation.Arguments.FirstOrDefault()?.Value.Type;
        if (configuration is null)
        {
            return false;
        }

        var owner = DunePaths.TryGetFeature(path, out var feature)
            ? "TemplateApp.Features." + feature + "."
            : "TemplateApp.Infrastructure.";
        return (DuneSymbols.NamespaceOf(configuration) + ".").StartsWith(owner, StringComparison.Ordinal);
    }

    private static bool IsChecked(string path) =>
        DunePaths.IsBackendFile(path) && !DunePaths.IsMigration(path) && !DunePaths.IsTestsFile(path);

    private static bool IsInfrastructurePersistence(string path)
    {
        var marker = path.IndexOf(InfrastructurePersistence, StringComparison.Ordinal);
        return marker >= 0 && path.IndexOf('/', marker + InfrastructurePersistence.Length) < 0;
    }

    private static bool IsFeaturePersistence(string path, out string feature)
    {
        if (!DunePaths.TryGetFeature(path, out feature))
        {
            return false;
        }

        var segments = DunePaths.FeatureSegments(path, feature);
        return segments.Length == 2 && segments[0].Equals("Persistence", StringComparison.Ordinal);
    }
}
