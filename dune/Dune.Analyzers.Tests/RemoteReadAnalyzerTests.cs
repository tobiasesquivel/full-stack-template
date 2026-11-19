using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class RemoteReadAnalyzerTests
{
    private const string Platform = """
        using System;
        using System.Collections.Generic;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
        }
        namespace Microsoft.AspNetCore.Builder
        {
            using Microsoft.AspNetCore.Routing;
            public sealed class RouteHandlerBuilder { }
            public static class EndpointRouteBuilderExtensions
            {
                public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
            }
        }
        namespace TemplateApp.Features.Users.Contracts
        {
            public interface IUsersModule
            {
                Task<IReadOnlyDictionary<string, string>> FindDisplayNamesAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
            }
        }
        namespace TemplateApp.Features.Projects.Persistence
        {
            public sealed class ProjectsDbContext { }
        }
        namespace TemplateApp.Features.Projects
        {
            using TemplateApp.Features.Projects.Persistence;
            using TemplateApp.Features.Users.Contracts;

            public sealed class TaskCountsClient
            {
                private readonly HttpClient _http;

                public TaskCountsClient(HttpClient http)
                {
                    _http = http;
                }

                public Task<HttpResponseMessage> CountAsync(CancellationToken ct) => _http.GetAsync("internal/v1/task-counts", ct);
            }

            public sealed class ProjectResponses
            {
                public ProjectResponses(TaskCountsClient tasks, IUsersModule users) { }
            }

            public sealed class ProjectTaskCounters
            {
                public ProjectTaskCounters(ProjectsDbContext db) { }
            }

            public sealed class ProjectReadModels
            {
                public ProjectReadModels(ProjectTaskCounters counters, IUsersModule users) { }
            }
        }
        """;

    private static (string Path, string Source) Endpoint(string slice, string verb) =>
        ($"backend/Features/Projects/{slice}/{slice}Endpoint.cs", $$"""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            namespace TemplateApp.Features.Projects.{{slice}};
            public static class {{slice}}Endpoint
            {
                public static void Map(IEndpointRouteBuilder routes) => routes.{{verb}}("/", ({{slice}} slice) => 0);
            }
            """);

    private static (string Path, string Source) Slice(string slice, string dependencies) =>
        ($"backend/Features/Projects/{slice}/{slice}.cs", $$"""
            using System.Net.Http;
            using TemplateApp.Features.Projects.Persistence;
            using TemplateApp.Features.Users.Contracts;
            namespace TemplateApp.Features.Projects.{{slice}};
            public sealed class {{slice}}
            {
                public {{slice}}({{dependencies}}) { }
            }
            """);

    [Fact]
    public async Task AReadThatOnlyUsesItsContextItsProjectionAndAnInProcessModuleIsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            Endpoint("GetProject", "MapGet"),
            Slice("GetProject", "ProjectsDbContext db, ProjectTaskCounters counters, IUsersModule users"),
            Endpoint("ListProjects", "MapGet"),
            Slice("ListProjects", "ProjectsDbContext db, ProjectReadModels models"),
            ("backend/Features/Projects/ListProjects/ListProjectsTests.cs", """
                using TemplateApp.Features.Projects;
                namespace TemplateApp.Features.Projects.ListProjects;
                public sealed class ListProjectsTests
                {
                    public ListProjectsTests(TaskCountsClient tasks) { }
                }
                """));

        Assert.Empty(Findings(diagnostics));
    }

    [Fact]
    public async Task AReadThatReachesARemoteClientDirectlyOrThroughAFeatureTypeIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            Endpoint("GetProject", "MapGet"),
            Slice("GetProject", "ProjectsDbContext db, TaskCountsClient tasks"),
            Endpoint("ListProjects", "MapGet"),
            Slice("ListProjects", "ProjectsDbContext db, ProjectResponses responses"),
            Endpoint("FindProject", "MapGet"),
            Slice("FindProject", "HttpClient http"));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Projects/FindProject/FindProject.cs", "'Projects/FindProject' es una slice de lectura (su endpoint solo mapea GET) y llama por HTTP a otro proceso por FindProject -> HttpClient: una lectura no llama a otro proceso; leé su proyección local alimentada por eventos"),
                ("backend/Features/Projects/GetProject/GetProject.cs", "'Projects/GetProject' es una slice de lectura (su endpoint solo mapea GET) y llama por HTTP a otro proceso por GetProject -> TaskCountsClient: una lectura no llama a otro proceso; leé su proyección local alimentada por eventos"),
                ("backend/Features/Projects/ListProjects/ListProjects.cs", "'Projects/ListProjects' es una slice de lectura (su endpoint solo mapea GET) y llama por HTTP a otro proceso por ListProjects -> ProjectResponses -> TaskCountsClient: una lectura no llama a otro proceso; leé su proyección local alimentada por eventos"),
            },
            Findings(diagnostics));
    }

    [Fact]
    public async Task AWriteSliceMayCallAnotherProcess()
    {
        var diagnostics = await AnalyzeAsync(
            Endpoint("UpdateProject", "MapPut"),
            Slice("UpdateProject", "ProjectsDbContext db, ProjectResponses responses"),
            Endpoint("CreateProject", "MapPost"),
            Slice("CreateProject", "TaskCountsClient tasks"));

        Assert.Empty(Findings(diagnostics));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("backend/Platform.cs", Platform) },
            AnalyzerHost.Only<RemoteReadAnalyzer>());

    private static (string Path, string Message)[] Findings(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0024")
            .Select(diagnostic => (diagnostic.Location.SourceTree!.FilePath, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(finding => finding.FilePath, System.StringComparer.Ordinal)
            .ToArray();
}
