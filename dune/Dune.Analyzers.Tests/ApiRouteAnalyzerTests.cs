using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class ApiRouteAnalyzerTests
{
    private const string AspNetStub = """
        using System;
        using System.Collections.Generic;
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
            public sealed class RouteGroupBuilder : IEndpointRouteBuilder { }
        }
        namespace Microsoft.AspNetCore.Builder
        {
            using Microsoft.AspNetCore.Routing;
            public sealed class RouteHandlerBuilder { }
            public static class EndpointRouteBuilderExtensions
            {
                public static RouteGroupBuilder MapGroup(this IEndpointRouteBuilder endpoints, string prefix) => new();
                public static RouteHandlerBuilder MapGet(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPatch(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapDelete(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapMethods(this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> httpMethods, Delegate handler) => new();
                public static RouteHandlerBuilder Map(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
            }
        }
        """;

    private const string CreateTaskEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;
        namespace TemplateApp.Features.Tasks.CreateTask;
        public static class CreateTaskEndpoint
        {
            public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPost("", Handle);
            private static string Handle() => "created";
        }
        """;

    [Fact]
    public async Task GroupsUnderApiAndSlicesThatMapOnTheirGroupAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                using TemplateApp.Features.Tasks.CreateTask;
                namespace TemplateApp.Features.Tasks;
                public static class TasksFeature
                {
                    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
                    {
                        RouteGroupBuilder tasks = app.MapGroup("/api/v1/tasks");
                        CreateTaskEndpoint.Map(tasks);
                        RouteGroupBuilder comments = tasks.MapGroup("/{id}/comments");
                        comments.MapPost("", () => "comment");
                        app.MapGroup("/api");
                        app.MapGet("/openapi/v1.json", () => "document");
                        return app;
                    }
                }
                """),
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", CreateTaskEndpoint));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GroupOutsideApiOrWithoutALiteralPrefixIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Webhooks/WebhooksFeature.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Webhooks;
                public static class WebhooksFeature
                {
                    public static IEndpointRouteBuilder MapWebhooksEndpoints(this IEndpointRouteBuilder app, string prefix)
                    {
                        app.MapGroup("/webhooks");
                        app.MapGroup("/apiary");
                        app.MapGroup(prefix);
                        return app;
                    }
                }
                """));

        Assert.Equal(new[] { 8, 9, 10 }, Lines(diagnostics));
        Assert.All(diagnostics, diagnostic => Assert.Contains("/api", diagnostic.GetMessage(), System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task InternalGroupIsAcceptedOnlyInInternalApi()
    {
        const string InternalGroup = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            namespace TemplateApp.Infrastructure.Http.Remote;
            public static class InternalApi
            {
                public const string Prefix = "/internal/v1";
                public static RouteGroupBuilder MapInternalApi(this IEndpointRouteBuilder app) => app.MapGroup(Prefix);
            }
            """;

        var accepted = await AnalyzeAsync(("backend/Infrastructure/Http/Remote/InternalApi.cs", InternalGroup));
        var rejected = await AnalyzeAsync(
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Tasks;
                public static class TasksFeature
                {
                    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
                    {
                        app.MapGroup("/internal/v1");
                        app.MapGroup("/internal/v1/tasks");
                        return app;
                    }
                }
                """),
            ("backend/Infrastructure/Http/Remote/OtherApi.cs", InternalGroup.Replace("class InternalApi", "class OtherApi", System.StringComparison.Ordinal)),
            ("backend/Infrastructure/Http/Remote/Sub/InternalApi.cs", InternalGroup.Replace("Http.Remote;", "Http.Remote.Sub;", System.StringComparison.Ordinal)),
            ("backend/Infrastructure/Http/InternalApi.cs", InternalGroup.Replace("Http.Remote;", "Http;", System.StringComparison.Ordinal)));

        Assert.Empty(accepted);
        Assert.Equal(new[] { 7, 7, 7, 8, 9 }, Lines(rejected));
    }

    [Fact]
    public async Task UnsafeRouteOnTheHostIsRejectedWhileGetIsAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Hosts/Monolith/Program.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                public static class Host
                {
                    public static void Map(IEndpointRouteBuilder app)
                    {
                        app.MapGet("/status", () => "ok");
                        app.MapPost("/webhooks/stripe", () => "paid");
                        app.MapPut("/api/v1/raw", () => "put");
                        app.MapPatch("/api/v1/raw", () => "patch");
                        app.MapDelete("/api/v1/raw", () => "delete");
                        app.MapMethods("/api/v1/raw", new[] { "POST" }, () => "post");
                        app.Map("/api/v1/raw", () => "any");
                    }
                }
                """));

        Assert.Equal(new[] { 8, 9, 10, 11, 12, 13 }, Lines(diagnostics));
    }

    [Theory]
    [InlineData("Access")]
    [InlineData("MessageNames")]
    [InlineData("Telemetry")]
    [InlineData("Contracts")]
    public async Task ARouteOnTheFeatureGroupInATechnicalFolderIsRejected(string folder)
    {
        var diagnostics = await AnalyzeAsync(
            ($"backend/Features/Tasks/{folder}/{folder}Endpoint.cs", $$"""
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Tasks.{{folder}};
                public static class {{folder}}Routes
                {
                    public static void Map(RouteGroupBuilder tasks)
                    {
                        tasks.MapGet("/session", () => "ok");
                        tasks.MapPost("/session", () => "ok");
                        tasks.MapGroup("/sessions");
                    }
                }
                """));

        Assert.Equal(new[] { 8, 9, 10 }, Lines(diagnostics));
    }

    [Fact]
    public async Task ARouteInATechnicalFolderIsRejectedEvenIfTheFileLooksLikeAMigration()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/Access/AccessModelSnapshotRoutes.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Tasks.Access;
                public static class AccessModelSnapshotRoutes
                {
                    public static void Map(RouteGroupBuilder tasks) => tasks.MapGet("/session", () => "ok");
                }
                """));

        Assert.Equal(new[] { 6 }, Lines(diagnostics));
    }

    [Fact]
    public async Task SliceThatReceivesTheHostInsteadOfItsGroupIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Microsoft.AspNetCore.Routing;
                using TemplateApp.Features.Tasks.CreateTask;
                namespace TemplateApp.Features.Tasks;
                public static class TasksFeature
                {
                    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
                    {
                        CreateTaskEndpoint.Map(app);
                        return app;
                    }
                }
                """),
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", CreateTaskEndpoint));

        Assert.Equal(new[] { 8 }, Lines(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("stubs/AspNetCore.cs", AspNetStub) },
            AnalyzerHost.Only<ApiRouteAnalyzer>());

    private static int[] Lines(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0011")
            .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
            .OrderBy(line => line)
            .ToArray();
}
