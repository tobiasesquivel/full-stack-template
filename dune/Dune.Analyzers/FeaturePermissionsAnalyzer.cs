using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FeaturePermissionsAnalyzer : DiagnosticAnalyzer
{
    private const string DeclarationInterface = "TemplateApp.Infrastructure.Access.IFeaturePermissions";
    private const string TestsProject = "backend/Tests/";
    private const string Suffix = "Permissions";

    private static readonly Regex Permission = new("^Permissions\\.([A-Za-z0-9_]+)\\.", RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.FeaturePermissions);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (IsTestsProject(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
            {
                return;
            }

            var contract = start.Compilation.GetTypeByMetadataName(DeclarationInterface);
            if (contract is not null)
            {
                start.RegisterSymbolAction(symbol => AnalyzeDeclaration(symbol, contract), SymbolKind.NamedType);
            }

            start.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.StringLiteralExpression);
        });
    }

    private static void AnalyzeDeclaration(SymbolAnalysisContext context, INamedTypeSymbol contract)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default))
        {
            ReportUndiscoverable(context, type);
            return;
        }

        foreach (var location in type.Locations.Where(location => location.IsInSource))
        {
            var problem = Problem(type, DunePaths.Normalize(location.SourceTree?.FilePath));
            if (problem is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.FeaturePermissions, location, type.Name, problem));
            }
        }
    }

    private static void ReportUndiscoverable(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            return;
        }

        foreach (var location in type.Locations.Where(location => location.IsInSource))
        {
            if (IsDeclarationFile(DunePaths.Normalize(location.SourceTree?.FilePath), out _))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuneDiagnostics.FeaturePermissions,
                    location,
                    type.Name,
                    "está en el archivo de la declaración y no implementa IFeaturePermissions: el host no la descubre y Auth nunca recibe esos permisos"));
            }
        }
    }

    private static string? Problem(INamedTypeSymbol type, string path)
    {
        if (type.ContainingType is not null || type.Arity != 0 || type.IsFileLocal || !IsDeclarationFile(path, out var feature))
        {
            return "implementa IFeaturePermissions y no es la clase <Feature>Permissions de primer nivel, no genérica ni file, de Features/<Feature>/Access/<Feature>Permissions.cs: el host toma el nombre de la feature del nombre de la clase";
        }

        if (!type.Name.Equals(feature + Suffix, StringComparison.Ordinal))
        {
            return "tiene que llamarse " + feature + Suffix + ": el host toma el nombre de la feature del nombre de la clase";
        }

        if (type.TypeKind != TypeKind.Class || type.IsStatic || type.IsAbstract || !type.IsSealed)
        {
            return "no es una clase sealed que el host pueda crear: la declaración es internal sealed class <Feature>Permissions : IFeaturePermissions";
        }

        if (!type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public))
        {
            return "no tiene un constructor público sin parámetros: el host no la puede crear";
        }

        return null;
    }

    private static void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        var match = Permission.Match(literal.Token.ValueText);
        if (!match.Success)
        {
            return;
        }

        var owner = match.Groups[1].Value;
        var path = DunePaths.Normalize(literal.SyntaxTree.FilePath);
        if (IsDeclarationFile(path, out var feature) && feature.Equals(owner, StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DuneDiagnostics.FeaturePermissions,
            literal.GetLocation(),
            literal.Token.ValueText,
            "es un permiso de " + owner + " y solo se escribe en backend/Features/" + owner + "/Access/" + owner + Suffix + ".cs: su feature usa la constante, y Auth recibe la declaración sin copiarla"));
    }

    private static bool IsDeclarationFile(string path, out string feature) =>
        DunePaths.TryGetFeature(path, out feature)
        && DunePaths.FeatureSegments(path, feature) is { Length: 2 } segments
        && segments[0].Equals("Access", StringComparison.Ordinal)
        && segments[1].Equals(feature + Suffix + ".cs", StringComparison.Ordinal);

    private static bool IsTestsProject(AnalyzerConfigOptions options) =>
        options.TryGetValue(HostBoundaryAnalyzer.ProjectPathOption, out var projectPath)
        && DunePaths.Normalize(projectPath).TrimStart('/').StartsWith(TestsProject, StringComparison.OrdinalIgnoreCase);
}
