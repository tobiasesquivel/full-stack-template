using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class SliceShapeAnalyzerTests
{
    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string path, string source) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)> { (path, source) },
            AnalyzerHost.Only<NamingAnalyzer>());

    [Fact]
    public async Task SliceInItsFolderIsAccepted()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/CreateTask/CreateTaskRequest.cs", """
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public sealed record CreateTaskRequest(string Title);
                    """),
                ("backend/Features/Tasks/CreateTask/CreateTaskRequestValidator.cs", """
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public sealed class CreateTaskRequestValidator { }
                    """),
                ("backend/Features/Tasks/CreateTask/CreateTask.cs", """
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public sealed class CreateTask { }
                    """),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public static class CreateTaskEndpoint { }
                    """),
                ("backend/Features/Tasks/CreateTask/CreateTaskTests.cs", """
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public sealed class CreateTaskTests { }
                    """),
                ("backend/Features/Tasks/TasksFeature.cs", """
                    namespace TemplateApp.Features.Tasks;
                    public static class TasksFeature { }
                    """),
            },
            AnalyzerHost.Only<NamingAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "DUNE0005" or "DUNE0008");
    }

    [Fact]
    public async Task SharedModuleAtFeatureRootIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/TasksModule.cs", """
            namespace TemplateApp.Features.Tasks;
            public sealed partial class TasksModule { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task SharedEndpointsAtFeatureRootIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Users/UsersEndpoints.cs", """
            namespace TemplateApp.Features.Users;
            public static class UsersEndpoints { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task EndpointAtFeatureRootIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/CreateTaskEndpoint.cs", """
            namespace TemplateApp.Features.Tasks;
            public static class CreateTaskEndpoint { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task EndpointInContractsIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/Contracts/ContractsEndpoint.cs", """
            namespace TemplateApp.Features.Tasks.Contracts;
            public static class ContractsEndpoint { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Theory]
    [InlineData("MessageNames")]
    [InlineData("Telemetry")]
    [InlineData("Access")]
    public async Task EndpointInATechnicalFolderIsRejected(string folder)
    {
        var diagnostics = await AnalyzeAsync($"backend/Features/Tasks/{folder}/{folder}Endpoint.cs", $$"""
            namespace TemplateApp.Features.Tasks.{{folder}};
            public static class {{folder}}Endpoint { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task EndpointOfAnotherSliceIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/UpdateTask/CreateTaskEndpoint.cs", """
            namespace TemplateApp.Features.Tasks.UpdateTask;
            public static class CreateTaskEndpoint { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task FeatureInsideSliceIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/CreateTask/TasksFeature.cs", """
            namespace TemplateApp.Features.Tasks.CreateTask;
            public static class TasksFeature { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task FeatureNamedAfterAnotherFeatureIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/UsersFeature.cs", """
            namespace TemplateApp.Features.Tasks;
            public static class UsersFeature { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }

    [Fact]
    public async Task SliceEndpointRejectsDbContext()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public static class CreateTaskEndpoint
                    {
                        public static void Handle(DbContext db) => _ = db;
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
    }

    [Fact]
    public async Task SliceOperationAcceptsDbContext()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>
            {
                ("backend/Features/Tasks/CreateTask/CreateTask.cs", """
                    using Microsoft.EntityFrameworkCore;
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public sealed class CreateTask
                    {
                        private readonly DbContext _db;
                        public CreateTask(DbContext db) => _db = db;
                    }
                    """),
            },
            AnalyzerHost.Only<EndpointLayerAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0002");
    }

    [Fact]
    public async Task TestsTypeInsideSliceSourceIsRejected()
    {
        var diagnostics = await AnalyzeAsync("backend/Features/Tasks/CreateTask/CreateTask.cs", """
            namespace TemplateApp.Features.Tasks.CreateTask;
            public sealed class CreateTaskTests { }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DUNE0008");
    }
}
