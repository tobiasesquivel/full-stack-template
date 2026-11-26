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
    private const string Backend = "C:/repo/backend/";

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
    public async Task AProjectUnderHosts_ThatTheTableDoesNotKnow_Fails()
    {
        var diagnostics = await AnalyzeHostAsync("TemplateApp.Billing", "Hosts/Billing", new[] { "Hosts/Billing/Program.cs" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'backend/Hosts/Billing' es un host que la regla no conoce", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AHostAssembly_WithoutItsProjectDirectory_Fails(string? directory)
    {
        var diagnostics = await AnalyzeAsync("TemplateApp", directory, new[] { Backend + "Features/Tasks/Domain/TaskItem.cs" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'TemplateApp' es un host y la regla no puede leer la carpeta de su proyecto", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHostAssembly_OutsideBackendHosts_Fails()
    {
        var diagnostics = await AnalyzeAsync("TemplateApp", Backend.TrimEnd('/'), new[] { Backend + "Program.cs" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'TemplateApp' es un host y su proyecto está en C:/repo/backend", diagnostic.GetMessage(), StringComparison.Ordinal);
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
            : ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.ProjectDirectoryOption, projectDirectory.Replace('/', '\\'));

        return AnalyzerHost.AnalyzeProjectAsync(
            assembly,
            properties,
            files.Select((file, index) => (file, $"namespace Probe{index};\ninternal static class Probe{index} {{ }}\n")).ToList(),
            references.Select(Reference).ToList(),
            AnalyzerHost.Only<HostBoundaryAnalyzer>());
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(string assembly, string folder, IEnumerable<string> files, MetadataReference reference) =>
        AnalyzerHost.AnalyzeProjectAsync(
            assembly,
            ImmutableDictionary<string, string>.Empty.Add(HostBoundaryAnalyzer.ProjectDirectoryOption, Backend + folder),
            files.Select((file, index) => (Backend + file, $"namespace Probe{index};\ninternal static class Probe{index} {{ }}\n")).ToList(),
            new[] { reference },
            AnalyzerHost.Only<HostBoundaryAnalyzer>());

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
