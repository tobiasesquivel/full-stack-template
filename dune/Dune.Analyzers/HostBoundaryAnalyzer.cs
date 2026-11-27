using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dune.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string ProjectDirectoryOption = "build_property.MSBuildProjectDirectory";
    public const string ProjectPathOption = "build_property.DuneProjectPath";
    public const string IntermediateOutputOption = "build_property.IntermediateOutputPath";
    private const string HostsFolder = "backend/Hosts/";
    private const string PlatformAssembly = "TemplateApp.Platform";
    public const string FeaturesOption = "build_property.DuneHostFeatures";
    public const string ExcludedFeaturesOption = "build_property.DuneHostExcludedFeatures";
    private const string ProductAssemblyPrefix = "TemplateApp";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuneDiagnostics.HostBoundary);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        var assembly = context.Compilation.AssemblyName ?? string.Empty;
        var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        var paths = context.Compilation.SyntaxTrees.Select(tree => DunePaths.Normalize(tree.FilePath)).ToArray();
        if (!options.TryGetValue(ProjectDirectoryOption, out var directory) || string.IsNullOrWhiteSpace(directory)
            || !options.TryGetValue(ProjectPathOption, out var projectPath) || string.IsNullOrWhiteSpace(projectPath))
        {
            var anyHostFile = paths.FirstOrDefault(path => path.IndexOf("/" + HostsFolder, StringComparison.OrdinalIgnoreCase) >= 0);
            if (anyHostFile is not null)
            {
                Report(context, Location.None, assembly, $"compila {anyHostFile}, de un host, y la regla no puede leer la carpeta de su proyecto; Dune.props tiene que exponer MSBuildProjectDirectory y DuneProjectPath con CompilerVisibleProperty");
            }

            return;
        }

        var projectDirectory = DunePaths.Normalize(directory).TrimEnd('/');
        var relativeProject = DunePaths.Normalize(projectPath).Trim('/');
        if (!projectDirectory.EndsWith("/" + relativeProject, StringComparison.OrdinalIgnoreCase))
        {
            Report(context, Location.None, assembly, $"tiene DuneProjectPath {relativeProject}/ y su carpeta es {projectDirectory}; la regla no puede ubicar la raíz del repo");
            return;
        }

        var repositoryRoot = projectDirectory.Substring(0, projectDirectory.Length - relativeProject.Length);
        if (!relativeProject.StartsWith(HostsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var hostFile = paths.FirstOrDefault(path => Collapse(path).StartsWith(repositoryRoot + HostsFolder, StringComparison.OrdinalIgnoreCase));
            if (hostFile is not null)
            {
                Report(context, Location.None, assembly, $"compila {hostFile}, de un host, y su proyecto está en {relativeProject}/; cada host vive en {HostsFolder}<Host>/, con su .csproj en la raíz de esa carpeta");
            }
            else if (Names(options, FeaturesOption).Length > 0 || Names(options, ExcludedFeaturesOption).Length > 0)
            {
                Report(context, Location.None, assembly, $"declara DuneHostFeatures o DuneHostExcludedFeatures y su proyecto está en {relativeProject}/; un host vive en {HostsFolder}<Host>/, donde lo encuentran DUNE0032, check-host-items y check-codegen");
            }

            return;
        }

        var backendSlash = projectDirectory.Length - relativeProject.Length - 1 + "backend/".Length;
        var folder = projectDirectory.Substring(backendSlash + 1 + "Hosts/".Length);
        if (folder.Length == 0 || folder.IndexOf('/') >= 0)
        {
            Report(context, Location.None, HostsFolder + folder, $"no es la carpeta de un host; cada host vive en {HostsFolder}<Host>/, con su .csproj en la raíz de esa carpeta");
            return;
        }

        if (!TryReadHost(options, folder, out var host, out var problem))
        {
            Report(context, Location.None, HostsFolder + folder, problem);
            return;
        }

        var intermediate = options.TryGetValue(IntermediateOutputOption, out var output) && !string.IsNullOrWhiteSpace(output)
            ? Collapse(Rooted(DunePaths.Normalize(output), projectDirectory)).TrimEnd('/') + "/"
            : null;
        AnalyzeHost(context, projectDirectory.Substring(0, backendSlash + 1), projectDirectory.Substring(backendSlash + 1) + "/", host, intermediate);
    }

    private static bool TryReadHost(AnalyzerConfigOptions options, string folder, out Host host, out string problem)
    {
        var features = Names(options, FeaturesOption);
        var excluded = Names(options, ExcludedFeaturesOption);
        host = null!;
        problem = string.Empty;
        if (features.Length > 0 == excluded.Length > 0)
        {
            problem = features.Length > 0
                ? "declara DuneHostFeatures y DuneHostExcludedFeatures; su .csproj declara una sola: las features que despliega o las que deja afuera"
                : "no declara qué features despliega; su .csproj lleva <DuneHostFeatures>A,B</DuneHostFeatures> (solo esas) o <DuneHostExcludedFeatures>A</DuneHostExcludedFeatures> (todas salvo esas)";
            return false;
        }

        host = features.Length > 0
            ? new Host(folder, $"el host compila su carpeta y Features/{string.Join(", Features/", features)} (DuneHostFeatures de su .csproj)", feature => features.Contains(feature, StringComparer.Ordinal), features)
            : new Host(folder, $"el host compila su carpeta y Features salvo Features/{string.Join(", Features/", excluded)} (DuneHostExcludedFeatures de su .csproj)", feature => !excluded.Contains(feature, StringComparer.OrdinalIgnoreCase), Array.Empty<string>());
        return true;
    }

    private static string[] Names(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value) && value is not null
            ? value.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0).ToArray()
            : Array.Empty<string>();

    private static void AnalyzeHost(CompilationAnalysisContext context, string backendRoot, string ownFolder, Host host, string? intermediate)
    {
        var repositoryRoot = backendRoot.Substring(0, backendRoot.Length - "backend/".Length);
        if (intermediate is not null
            && intermediate.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase)
            && !intermediate.StartsWith(backendRoot + ownFolder, StringComparison.OrdinalIgnoreCase))
        {
            intermediate = null;
        }

        foreach (var reference in context.Compilation.References)
        {
            if (context.Compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly || assembly.Identity.Name == PlatformAssembly)
            {
                continue;
            }

            var file = reference is PortableExecutableReference { FilePath: { } path } ? Collapse(DunePaths.Normalize(path)) : null;
            var fromRepository = file is not null && file.StartsWith(repositoryRoot, StringComparison.OrdinalIgnoreCase);
            if (fromRepository || assembly.Identity.Name.StartsWith(ProductAssemblyPrefix, StringComparison.Ordinal))
            {
                Report(context, Location.None, HostsFolder + host.Folder, $"referencia {assembly.Identity.Name}{(fromRepository ? $" ({file})" : string.Empty)}; un host referencia solo {PlatformAssembly} entre los proyectos del repo, y lo compartido va en backend/Platform");
            }
        }

        var compiledFeatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            if (intermediate is not null && Collapse(DunePaths.Normalize(tree.FilePath)).StartsWith(intermediate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Relative(DunePaths.Normalize(tree.FilePath), backendRoot);
            if (relative is null)
            {
                Report(context, Location.Create(tree, default(TextSpan)), DunePaths.Normalize(tree.FilePath), $"está fuera de backend/; {host.Scope}, sin tests, y el resto llega por backend/Platform/TemplateApp.Platform.csproj");
            }
            else if (!Compiles(host, ownFolder, relative))
            {
                Report(context, Location.Create(tree, default(TextSpan)), "backend/" + relative, $"no le toca a {HostsFolder}{host.Folder}; {host.Scope}, sin tests, y el resto llega por backend/Platform/TemplateApp.Platform.csproj");
            }
            else if (FeatureOf(relative) is { } compiled)
            {
                compiledFeatures.Add(compiled);
            }
        }

        foreach (var declared in host.DeclaredFeatures.Where(declared => !compiledFeatures.Contains(declared)))
        {
            Report(context, Location.None, HostsFolder + host.Folder, $"declara {declared} en DuneHostFeatures y no compila ningún archivo de backend/Features/{declared}/; corregí el nombre en su .csproj o sacalo de la lista");
        }
    }

    private static string Rooted(string path, string projectDirectory) =>
        path.StartsWith("/", StringComparison.Ordinal) || (path.Length > 1 && path[1] == ':') ? path : projectDirectory + "/" + path;

    private static string? Relative(string path, string backendRoot)
    {
        var full = Collapse(path);
        return full.StartsWith(backendRoot, StringComparison.OrdinalIgnoreCase) ? full.Substring(backendRoot.Length) : null;
    }

    private static string Collapse(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 1)
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    private static bool Compiles(Host host, string ownFolder, string relative)
    {
        if (relative.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (relative.StartsWith(ownFolder, StringComparison.Ordinal))
        {
            return true;
        }

        return FeatureOf(relative) is { } feature && host.CompilesFeature(feature);
    }

    private static string? FeatureOf(string relative)
    {
        if (!relative.StartsWith("Features/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = relative.Substring("Features/".Length);
        var slash = rest.IndexOf('/');
        return slash > 0 ? rest.Substring(0, slash) : null;
    }

    private static void Report(CompilationAnalysisContext context, Location location, string subject, string detail) =>
        context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.HostBoundary, location, subject, detail));

    private sealed class Host
    {
        public Host(string folder, string scope, Func<string, bool> compilesFeature, string[] declaredFeatures)
        {
            DeclaredFeatures = declaredFeatures;
            Folder = folder;
            Scope = scope;
            CompilesFeature = compilesFeature;
        }

        public string Folder { get; }

        public string Scope { get; }

        public Func<string, bool> CompilesFeature { get; }

        public string[] DeclaredFeatures { get; }
    }
}
