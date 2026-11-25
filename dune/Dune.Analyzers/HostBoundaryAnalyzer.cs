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
    private const string HostsFolder = "backend/Hosts/";
    private const string PlatformAssembly = "TemplateApp.Platform";
    private const string ProductAssemblyPrefix = "TemplateApp";

    private static readonly (string Folder, string Assembly, string Scope, Func<string, bool> CompilesFeature)[] Hosts =
    {
        ("Monolith", "TemplateApp", "el monolito compila su carpeta y Features salvo Features/Tasks", feature => !feature.Equals("Tasks", StringComparison.OrdinalIgnoreCase)),
        ("Tasks", "TemplateApp.Tasks", "el host de Tasks compila su carpeta, Features/Tasks y Features/Health", feature => feature is "Tasks" or "Health"),
    };

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
        var isHostAssembly = Hosts.Any(host => host.Assembly == assembly);
        if (!context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(ProjectDirectoryOption, out var directory)
            || string.IsNullOrWhiteSpace(directory))
        {
            if (isHostAssembly)
            {
                Report(context, Location.None, assembly, "es un host y la regla no puede leer la carpeta de su proyecto; Dune.props tiene que exponer MSBuildProjectDirectory con CompilerVisibleProperty");
            }

            return;
        }

        var projectDirectory = DunePaths.Normalize(directory).TrimEnd('/');
        var hostsMarker = projectDirectory.LastIndexOf("/" + HostsFolder, StringComparison.Ordinal);
        if (hostsMarker < 0)
        {
            if (isHostAssembly)
            {
                Report(context, Location.None, assembly, $"es un host y su proyecto está en {projectDirectory}; cada host vive en {HostsFolder}<Host>/, y la tabla de HostBoundaryAnalyzer dice qué compila");
            }

            return;
        }

        AnalyzeHost(context, projectDirectory.Substring(0, hostsMarker + "/backend/".Length), projectDirectory.Substring(hostsMarker + 1 + HostsFolder.Length));
    }

    private static void AnalyzeHost(CompilationAnalysisContext context, string backendRoot, string folder)
    {
        var host = Hosts.FirstOrDefault(candidate => candidate.Folder.Equals(folder, StringComparison.Ordinal));
        if (host.Folder is null)
        {
            Report(context, Location.None, HostsFolder + folder, $"es un host que la regla no conoce; agregalo a la tabla de HostBoundaryAnalyzer con las features que compila ({string.Join(", ", Hosts.Select(known => HostsFolder + known.Folder))} ya están)");
            return;
        }

        foreach (var reference in context.Compilation.ReferencedAssemblyNames)
        {
            if (reference.Name.StartsWith(ProductAssemblyPrefix, StringComparison.Ordinal) && reference.Name != PlatformAssembly)
            {
                Report(context, Location.None, HostsFolder + host.Folder, $"referencia {reference.Name}; un host referencia solo {PlatformAssembly}, y lo compartido va en backend/Platform");
            }
        }

        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var relative = Relative(DunePaths.Normalize(tree.FilePath), backendRoot);
            if (relative is null)
            {
                Report(context, Location.Create(tree, default(TextSpan)), DunePaths.Normalize(tree.FilePath), $"está fuera de backend/; {host.Scope}, sin tests, y el resto llega por backend/Platform/TemplateApp.Platform.csproj");
            }
            else if (!Compiles(host, relative))
            {
                Report(context, Location.Create(tree, default(TextSpan)), "backend/" + relative, $"no le toca a {HostsFolder}{host.Folder}; {host.Scope}, sin tests, y el resto llega por backend/Platform/TemplateApp.Platform.csproj");
            }
        }
    }

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

    private static bool Compiles((string Folder, string Assembly, string Scope, Func<string, bool> CompilesFeature) host, string relative)
    {
        if (relative.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (relative.StartsWith("Hosts/" + host.Folder + "/", StringComparison.Ordinal))
        {
            return true;
        }

        if (!relative.StartsWith("Features/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = relative.Substring("Features/".Length);
        var slash = rest.IndexOf('/');
        return slash > 0 && host.CompilesFeature(rest.Substring(0, slash));
    }

    private static void Report(CompilationAnalysisContext context, Location location, string subject, string detail) =>
        context.ReportDiagnostic(Diagnostic.Create(DuneDiagnostics.HostBoundary, location, subject, detail));
}
