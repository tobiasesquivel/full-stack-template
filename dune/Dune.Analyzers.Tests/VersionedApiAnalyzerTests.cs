using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class VersionedApiAnalyzerTests
{
    private const string AspNetStub = """
        using System;
        namespace Microsoft.AspNetCore.Routing
        {
            public interface IEndpointRouteBuilder { }
            public sealed class RouteGroupBuilder : IEndpointRouteBuilder { }
        }
        namespace Asp.Versioning.Builder
        {
            using Microsoft.AspNetCore.Routing;
            public interface IVersionedEndpointRouteBuilder : IEndpointRouteBuilder { }
        }
        namespace Microsoft.AspNetCore.Builder
        {
            using Asp.Versioning.Builder;
            using Microsoft.AspNetCore.Routing;
            public sealed class RouteHandlerBuilder { }
            public static class EndpointRouteBuilderExtensions
            {
                public static RouteGroupBuilder MapGroup(this IEndpointRouteBuilder endpoints, string prefix) => new();
                public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
            }
            public static class IEndpointRouteBuilderExtensions
            {
                public static IVersionedEndpointRouteBuilder NewVersionedApi(this IEndpointRouteBuilder endpoints, string? name = null) => null!;
                public static RouteGroupBuilder WithApiVersionSet(this RouteGroupBuilder group, string? name = null) => group;
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
    public async Task FeaturesThatHangFromTheVersionedGroupAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Http/VersionedApi.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Infrastructure.Http;
                public static class VersionedApi
                {
                    public static RouteGroupBuilder MapVersionedApi(this IEndpointRouteBuilder app) =>
                        app.NewVersionedApi().MapGroup("/api/v{version:apiVersion}");
                }
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                using TemplateApp.Features.Tasks.CreateTask;
                namespace TemplateApp.Features.Tasks;
                public static class TasksFeature
                {
                    public static RouteGroupBuilder MapTasksEndpoints(this RouteGroupBuilder api)
                    {
                        RouteGroupBuilder tasks = api.MapGroup("/tasks");
                        CreateTaskEndpoint.Map(tasks);
                        tasks.MapGroup("/{id}/comments");
                        return api;
                    }
                }
                """),
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", CreateTaskEndpoint));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UnversionedGroupsAndVersionSetsOutsideInfrastructureAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Webhooks/WebhooksFeature.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Webhooks;
                public static class WebhooksFeature
                {
                    public static IEndpointRouteBuilder MapWebhooksEndpoints(this IEndpointRouteBuilder app)
                    {
                        app.MapGroup("/api/v1/webhooks");
                        app.NewVersionedApi("Webhooks").MapGroup("/api/v1/webhooks");
                        app.MapGroup("/api/v1/hooks").WithApiVersionSet("Hooks");
                        return app;
                    }
                }
                """));

        Assert.Equal(new[] { 6, 8, 9, 9, 10, 10 }, Lines(diagnostics));
    }

    [Fact]
    public async Task VersionedGroupWithAnotherPrefixIsRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Http/VersionedApi.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Infrastructure.Http;
                public static class VersionedApi
                {
                    public static RouteGroupBuilder MapVersionedApi(this IEndpointRouteBuilder app) =>
                        app.NewVersionedApi().MapGroup("/api/v1");
                }
                """));

        Assert.Equal(new[] { 7 }, Lines(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("stubs/AspNetCore.cs", AspNetStub) },
            AnalyzerHost.Only<VersionedApiAnalyzer>());

    private static int[] Lines(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0012")
            .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
            .OrderBy(line => line)
            .ToArray();
}
