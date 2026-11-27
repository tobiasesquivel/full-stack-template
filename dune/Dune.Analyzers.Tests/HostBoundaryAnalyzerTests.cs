using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class HostBoundaryAnalyzerTests
{
    private const string Repository = "C:/repo/";
    private const string Backend = Repository + "backend/";

    public static TheoryData<string, string> FilesOutsideTheMonolith => new()
    {
        { "Features/Tasks/Domain/TaskItem.cs", "backend/Features/Tasks/Domain/TaskItem.cs" },
        { "Features/tasks/Domain/TaskItem.cs", "backend/Features/tasks/Domain/TaskItem.cs" },
        { "Features/./Tasks/Domain/TaskItem.cs", "backend/Features/Tasks/Domain/TaskItem.cs" },
        { "Hosts/Monolith/../../Features/Health/../Tasks/Domain/TaskItem.cs", "backend/Features/Tasks/Domain/TaskItem.cs" },
        { "Infrastructure/Persistence/ContextRegistration.cs", "backend/Infrastructure/Persistence/ContextRegistration.cs" },
        { "Shared/Domain/IDomainEvent.cs", "backend/Shared/Domain/IDomainEvent.cs" },
        { "Tests/Support/Rows/AuditRow.cs", "backend/Tests/Support/Rows/AuditRow.cs" },
        { "Features/Users/UpdateProfile/UpdateProfileTests.cs", "backend/Features/Users/UpdateProfile/UpdateProfileTests.cs" },
        { "Hosts/Monolith/ProbeTests.CS", "backend/Hosts/Monolith/ProbeTests.CS" },
        { "Hosts/Tasks/Program.cs", "backend/Hosts/Tasks/Program.cs" },
        { "Hosts/monolith/Program.cs", "backend/Hosts/monolith/Program.cs" },
    };

    public static TheoryData<string> FilesOutsideTheTasksHost => new()
    {
        "Features/Users/UpdateProfile/UpdateProfileCommand.cs",
        "Features/health/GetLiveness/GetLivenessEndpoint.cs",
        "Features/Tasks/CreateTask/CreateTaskTests.cs",
        "Hosts/tasks/Program.cs",
        "Hosts/Monolith/Program.cs",
        "Infrastructure/Http/Remote/RemoteServices.cs",
    };

    [Fact]
    public async Task TheMonolith_CompilesItsFolderAndEveryFeatureButTasks()
    {
        var diagnostics = await AnalyzeHostAsync(
            "TemplateApp",
            "Hosts/Monolith",
            new[]
            {
                "Hosts/Monolith/Program.cs",
                "Hosts/Monolith/Internal/Generated/WolverineHandlers/CreateProjectHandler.cs",
                "Hosts/Monolith/obj/Release/net10.0/TemplateApp.GlobalUsings.g.cs",
                "Features/Users/UpdateProfile/UpdateProfileCommand.cs",
                "Features/Auth/AuthFeature.cs",
                "Features/Health/GetLiveness/GetLivenessEndpoint.cs",
            },
            "TemplateApp.Platform");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TheTasksHost_CompilesItsFolderTasksAndHealth()
    {
        var diagnostics = await AnalyzeHostAsync(
            "TemplateApp.Tasks",
            "Hosts/Tasks",
            new[]
            {
                "Hosts/Tasks/Program.cs",
                "Features/Tasks/CreateTask/CreateTaskCommand.cs",
                "Features/Health/GetLiveness/GetLivenessEndpoint.cs",
            },
            "TemplateApp.Platform");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [MemberData(nameof(FilesOutsideTheMonolith))]
    public async Task TheMonolith_RejectsAFileThatIsNotItsOwn(string file, string reported)
    {
        var diagnostics = await AnalyzeHostAsync("TemplateApp", "Hosts/Monolith", new[] { "Hosts/Monolith/Program.cs", file });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0032", diagnostic.Id);
        Assert.Contains($"'{reported}' no le toca a backend/Hosts/Monolith", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FilesOutsideTheTasksHost))]
    public async Task TheTasksHost_RejectsAFileThatIsNotItsOwn(string file)
    {
        var diagnostics = await AnalyzeHostAsync("TemplateApp.Tasks", "Hosts/Tasks", new[] { "Hosts/Tasks/Program.cs", file });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("DUNE0032", diagnostic.Id);
        Assert.Contains($"'backend/{file}' no le toca a backend/Hosts/Tasks", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHost_RejectsAFileOutsideTheBackend()
    {
        var diagnostics = await AnalyzeAsync(
            "TemplateApp.Tasks",
            Backend + "Hosts/Tasks",
            new[] { Backend + "Hosts/Tasks/Program.cs", "D:/elsewhere/backend/Features/Users/Probe.cs" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'D:/elsewhere/backend/Features/Users/Probe.cs' está fuera de backend/", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TemplateApp", "Hosts/Monolith", "TemplateApp.Tasks")]
    [InlineData("TemplateApp.Tasks", "Hosts/Tasks", "TemplateApp")]
    [InlineData("TemplateApp.Tasks", "Hosts/Tasks", "TemplateApp.Gateway")]
    public async Task AHost_ReferencesOnlyPlatform(string assembly, string folder, string reference)
    {
        var diagnostics = await AnalyzeHostAsync(assembly, folder, new[] { folder + "/Program.cs" }, "TemplateApp.Platform", reference);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains($"'backend/{folder}' referencia {reference}; un host referencia solo TemplateApp.Platform", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHost_RejectsAProjectOfTheRepository_WhateverItsName()
    {
        var diagnostics = await AnalyzeProjectAsync(
            "TemplateApp.Tasks",
            "Hosts/Tasks",
            new[] { "Hosts/Tasks/Program.cs" },
            FileReference("Billing.Core", "C:/repo/backend/Billing/bin/Release/net10.0/Billing.Core.dll"));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Hosts/Tasks' referencia Billing.Core (C:/repo/backend/Billing/bin/Release/net10.0/Billing.Core.dll)", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHost_AcceptsAPackageFromOutsideTheRepository()
    {
        var diagnostics = await AnalyzeProjectAsync(
            "TemplateApp.Tasks",
            "Hosts/Tasks",
            new[] { "Hosts/Tasks/Program.cs" },
            FileReference("Billing.Core", "C:/nuget/packages/billing.core/1.0.0/lib/net10.0/Billing.Core.dll"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AHost_AcceptsItsIntermediateOutput_OutsideTheRepository()
    {
        var diagnostics = await AnalyzeWithIntermediateAsync(
            "D:/artifacts/obj/TemplateApp/release/",
            Backend + "Hosts/Monolith/Program.cs",
            "D:/artifacts/obj/TemplateApp/release/TemplateApp.GlobalUsings.g.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AHost_IgnoresAnIntermediateOutput_ThatPointsIntoAnotherFolderOfTheRepository()
    {
        var diagnostics = await AnalyzeWithIntermediateAsync(
            "../../Features/Tasks/",
            Backend + "Hosts/Monolith/Program.cs",
            Backend + "Features/Tasks/Domain/TaskItem.cs");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Features/Tasks/Domain/TaskItem.cs' no le toca a backend/Hosts/Monolith", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInventedHost_IsCheckedWithItsOwnDeclaration_WithoutEditingTheRule()
    {
        var declaration = ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.FeaturesOption, "Invoices,Health");

        var accepted = await AnalyzeInventedHostAsync(
            declaration,
            "Hosts/Billing",
            "Hosts/Billing/Program.cs",
            "Features/Invoices/IssueInvoice/IssueInvoiceCommand.cs",
            "Features/Health/GetLiveness/GetLivenessEndpoint.cs");
        var rejected = await AnalyzeInventedHostAsync(
            declaration,
            "Hosts/Billing",
            "Hosts/Billing/Program.cs",
            "Features/Tasks/Domain/TaskItem.cs");

        Assert.Empty(accepted);
        var diagnostic = Assert.Single(rejected);
        Assert.Contains("'backend/Features/Tasks/Domain/TaskItem.cs' no le toca a backend/Hosts/Billing; el host compila su carpeta y Features/Invoices, Features/Health (DuneHostFeatures de su .csproj)", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHost_ThatDeclaresAFeatureItDoesNotCompile_Fails()
    {
        var diagnostics = await RunAsync(
            "Acme.Billing",
            ImmutableDictionary<string, string>.Empty
                .Add(HostBoundaryAnalyzer.FeaturesOption, "Invoice,Health")
                .Add(HostBoundaryAnalyzer.ProjectDirectoryOption, Backend + "Hosts/Billing"),
            new[] { Backend + "Hosts/Billing/Program.cs", Backend + "Features/Health/HealthFeature.cs" },
            new[] { Reference("TemplateApp.Platform") },
            compileDeclaredFeatures: false);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Hosts/Billing' declara Invoice en DuneHostFeatures y no compila ningún archivo de backend/Features/Invoice/", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHostBuiltThroughAnUppercasePath_IsStillChecked()
    {
        var diagnostics = await RunAsync(
            "TemplateApp.Tasks",
            ImmutableDictionary<string, string>.Empty
                .Add(HostBoundaryAnalyzer.FeaturesOption, "Tasks,Health")
                .Add(HostBoundaryAnalyzer.ProjectDirectoryOption, "C:\\REPO\\BACKEND\\HOSTS\\TASKS")
                .Add(HostBoundaryAnalyzer.ProjectPathOption, "BACKEND/HOSTS/TASKS/"),
            new[]
            {
                "C:/REPO/BACKEND/HOSTS/TASKS/Program.cs",
                "C:/REPO/BACKEND/Features/Tasks/TasksFeature.cs",
                "C:/REPO/BACKEND/Features/Health/HealthFeature.cs",
                "C:/REPO/BACKEND/Features/Users/UsersFeature.cs",
            },
            new[] { Reference("TemplateApp.Platform") });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Features/Users/UsersFeature.cs' no le toca a backend/Hosts/TASKS", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepositoryClonedUnderAnotherBackendHostsFolder_IsNotTakenForAHost()
    {
        var diagnostics = await RunAsync(
            "TemplateApp.Platform",
            ImmutableDictionary<string, string>.Empty
                .Add(HostBoundaryAnalyzer.ProjectDirectoryOption, "C:/backend/Hosts/clones/repo/backend/Platform")
                .Add(HostBoundaryAnalyzer.ProjectPathOption, "backend/Platform/"),
            new[] { "C:/backend/Hosts/clones/repo/backend/Infrastructure/Http/Remote/RemoteServices.cs" },
            Array.Empty<MetadataReference>());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task TheMonolith_TakesAnInventedFeature_WithoutEditingItsDeclaration()
    {
        var diagnostics = await AnalyzeHostAsync(
            "TemplateApp",
            "Hosts/Monolith",
            new[] { "Hosts/Monolith/Program.cs", "Features/Invoices/IssueInvoice/IssueInvoiceCommand.cs" },
            "TemplateApp.Platform");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(null, null, "no declara qué features despliega; su .csproj lleva <DuneHostFeatures>")]
    [InlineData(" , ", null, "no declara qué features despliega")]
    [InlineData("Invoices", "Tasks", "declara DuneHostFeatures y DuneHostExcludedFeatures")]
    public async Task AHost_ThatDoesNotDeclareExactlyOneList_Fails(string? features, string? excluded, string expected)
    {
        var declaration = ImmutableDictionary<string, string>.Empty;
        if (features is not null)
        {
            declaration = declaration.Add(HostBoundaryAnalyzer.FeaturesOption, features);
        }

        if (excluded is not null)
        {
            declaration = declaration.Add(HostBoundaryAnalyzer.ExcludedFeaturesOption, excluded);
        }

        var diagnostics = await AnalyzeInventedHostAsync(declaration, "Hosts/Billing", "Hosts/Billing/Program.cs");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Hosts/Billing' " + expected, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectNestedInsideAHostFolder_Fails()
    {
        var diagnostics = await AnalyzeInventedHostAsync(
            ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.FeaturesOption, "Invoices"),
            "Hosts/Billing/Api",
            "Hosts/Billing/Api/Program.cs");

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Hosts/Billing/Api' no es la carpeta de un host", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AProjectThatCompilesHostFiles_WithoutItsProjectDirectory_Fails(string? directory)
    {
        var diagnostics = await AnalyzeAsync("Acme.Shop", directory, new[] { Backend + "Hosts/Monolith/Program.cs" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Acme.Shop' compila C:/repo/backend/Hosts/Monolith/Program.cs, de un host, y la regla no puede leer la carpeta de su proyecto", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectThatCompilesHostFiles_OutsideBackendHosts_Fails()
    {
        var diagnostics = await AnalyzeAsync("Acme.Shop", Backend.TrimEnd('/'), new[] { Backend + "Hosts/Monolith/Program.cs" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Acme.Shop' compila C:/repo/backend/Hosts/Monolith/Program.cs, de un host, y su proyecto está en backend/", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectThatIsNotAHost_IsNotChecked()
    {
        var diagnostics = await AnalyzeAsync(
            "TemplateApp.Platform",
            Backend + "Platform",
            new[] { Backend + "Infrastructure/Http/Remote/RemoteServices.cs", Backend + "Shared/Domain/IDomainEvent.cs" });

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeHostAsync(string assembly, string folder, IEnumerable<string> files, params string[] references) =>
        AnalyzeAsync(assembly, Backend + folder, files.Select(file => Backend + file), references);

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string assembly, string? projectDirectory, IEnumerable<string> files, params string[] references)
    {
        var properties = projectDirectory is null
            ? ImmutableDictionary<string, string>.Empty
            : Declared(projectDirectory).Add(HostBoundaryAnalyzer.ProjectDirectoryOption, projectDirectory.Replace('/', '\\'));
        return RunAsync(assembly, properties, files, references.Select(Reference));
    }

    private static ImmutableDictionary<string, string> Declared(string projectDirectory) =>
        projectDirectory.EndsWith("Hosts/Monolith", StringComparison.Ordinal) ? ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.ExcludedFeaturesOption, "Tasks")
        : projectDirectory.EndsWith("Hosts/Tasks", StringComparison.Ordinal) ? ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.FeaturesOption, "Tasks, Health")
        : ImmutableDictionary<string, string>.Empty;

    private static Task<ImmutableArray<Diagnostic>> AnalyzeInventedHostAsync(ImmutableDictionary<string, string> declaration, string folder, params string[] files) =>
        RunAsync(
            "Acme.Billing",
            declaration.Add(HostBoundaryAnalyzer.ProjectDirectoryOption, Backend + folder),
            files.Select(file => Backend + file),
            new[] { Reference("TemplateApp.Platform") });

    private static Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(string assembly, string folder, IEnumerable<string> files, MetadataReference reference) =>
        RunAsync(
            assembly,
            Declared(Backend + folder).Add(HostBoundaryAnalyzer.ProjectDirectoryOption, Backend + folder),
            files.Select(file => Backend + file),
            new[] { reference });

    private static Task<ImmutableArray<Diagnostic>> AnalyzeWithIntermediateAsync(string intermediate, params string[] files) =>
        RunAsync(
            "TemplateApp",
            Declared(Backend + "Hosts/Monolith")
                .Add(HostBoundaryAnalyzer.ProjectDirectoryOption, Backend + "Hosts/Monolith")
                .Add(HostBoundaryAnalyzer.IntermediateOutputOption, intermediate),
            files,
            new[] { Reference("TemplateApp.Platform") });

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string assembly, ImmutableDictionary<string, string> properties, IEnumerable<string> files, IEnumerable<MetadataReference> references, bool compileDeclaredFeatures = true)
    {
        var all = files.ToList();
        if (properties.TryGetValue(HostBoundaryAnalyzer.ProjectDirectoryOption, out var projectDirectory)
            && !properties.ContainsKey(HostBoundaryAnalyzer.ProjectPathOption)
            && projectDirectory.Replace('\\', '/').StartsWith(Repository, StringComparison.OrdinalIgnoreCase))
        {
            properties = properties.Add(HostBoundaryAnalyzer.ProjectPathOption, projectDirectory.Replace('\\', '/').Substring(Repository.Length) + "/");
        }

        if (compileDeclaredFeatures
            && properties.TryGetValue(HostBoundaryAnalyzer.ProjectDirectoryOption, out var directory)
            && properties.TryGetValue(HostBoundaryAnalyzer.FeaturesOption, out var declared))
        {
            var backend = directory.Replace('\\', '/');
            backend = backend.Substring(0, backend.LastIndexOf("/Hosts/", StringComparison.Ordinal) + 1);
            foreach (var feature in declared.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0))
            {
                if (!all.Any(file => file.Replace('\\', '/').Contains($"/Features/{feature}/", StringComparison.Ordinal) && !file.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)))
                {
                    all.Add($"{backend}Features/{feature}/{feature}Feature.cs");
                }
            }
        }

        return AnalyzerHost.AnalyzeProjectAsync(
            assembly,
            properties,
            all.Select((file, index) => (file, $"namespace Probe{index};\ninternal static class Probe{index} {{ }}\n")).ToList(),
            references.ToList(),
            AnalyzerHost.Only<HostBoundaryAnalyzer>());
    }

    private static MetadataReference FileReference(string assembly, string path)
    {
        using var image = new MemoryStream();
        var emitted = CSharpCompilation.Create(assembly, references: TestReferences.All(), options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(image);
        Assert.True(emitted.Success);
        return MetadataReference.CreateFromImage(image.ToArray(), filePath: path);
    }

    private static MetadataReference Reference(string assembly) =>
        CSharpCompilation.Create(assembly, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference();
}
