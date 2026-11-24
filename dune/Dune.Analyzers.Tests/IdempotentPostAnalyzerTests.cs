using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class IdempotentPostAnalyzerTests
{
    private const string AspNetStub = """
        using System;
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
                public static RouteHandlerBuilder MapPost(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
                public static RouteHandlerBuilder MapPut(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => new();
            }
            public static class RoutingEndpointConventionBuilderExtensions
            {
                public static RouteHandlerBuilder WithName(this RouteHandlerBuilder builder, string name) => builder;
            }
        }
        namespace TemplateApp.Infrastructure.Http.Idempotency
        {
            using Microsoft.AspNetCore.Builder;
            public static class IdempotencyOptIn
            {
                public static RouteHandlerBuilder WithIdempotency<TRequest>(this RouteHandlerBuilder builder) => builder;
            }
        }
        """;

    [Fact]
    public async Task PostsThatChainWithIdempotencyAndAuthPostsAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                using TemplateApp.Infrastructure.Http.Idempotency;
                namespace TemplateApp.Features.Tasks.CreateTask;
                public sealed record CreateTaskRequest(string Title);
                public static class CreateTaskEndpoint
                {
                    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPost("", Handle).WithIdempotency<CreateTaskRequest>();
                    private static string Handle(CreateTaskRequest request) => "created";
                }
                """),
            ("backend/Features/Notes/CreateNote/CreateNoteEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                using TemplateApp.Infrastructure.Http.Idempotency;
                namespace TemplateApp.Features.Notes.CreateNote;
                public sealed record CreateNoteRequest(string Text);
                public static class CreateNoteEndpoint
                {
                    public static void Map(IEndpointRouteBuilder notes) =>
                        notes.MapPost("", Handle).WithName("CreateNote").WithIdempotency<CreateNoteRequest>();
                    private static string Handle(CreateNoteRequest request) => "created";
                }
                """),
            ("backend/Features/Auth/OpenSession/OpenSessionEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Auth.OpenSession;
                public static class OpenSessionEndpoint
                {
                    public static void Map(IEndpointRouteBuilder auth) => auth.MapPost("/login", Handle);
                    private static string Handle() => "opened";
                }
                """),
            ("backend/Features/Tasks/UpdateTask/UpdateTaskEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Tasks.UpdateTask;
                public static class UpdateTaskEndpoint
                {
                    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPut("/{id}", Handle);
                    private static string Handle() => "updated";
                }
                """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task PostsWithoutWithIdempotencyAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Tasks.CreateTask;
                public static class CreateTaskEndpoint
                {
                    public static void Map(IEndpointRouteBuilder tasks) => tasks.MapPost("", Handle);
                    private static string Handle() => "created";
                }
                """),
            ("backend/Features/Notes/CreateNote/CreateNoteEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                namespace TemplateApp.Features.Notes.CreateNote;
                public static class CreateNoteEndpoint
                {
                    public static void Map(IEndpointRouteBuilder notes)
                    {
                        RouteHandlerBuilder route = notes.MapPost("", Handle);
                        notes.MapPost("/drafts", Handle).WithName("CreateDraft");
                    }
                    private static string Handle() => "created";
                }
                """),
            ("backend/Infrastructure/Http/LegacyIdempotency.cs", """
                using Microsoft.AspNetCore.Builder;
                namespace TemplateApp.Infrastructure.Http;
                public static class LegacyIdempotency
                {
                    public static RouteHandlerBuilder WithIdempotency<TRequest>(this RouteHandlerBuilder builder) => builder;
                }
                """),
            ("backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs", """
                using Microsoft.AspNetCore.Builder;
                using Microsoft.AspNetCore.Routing;
                using TemplateApp.Infrastructure.Http;
                namespace TemplateApp.Features.Projects.CreateProject;
                public sealed record CreateProjectRequest(string Name);
                public static class CreateProjectEndpoint
                {
                    public static void Map(IEndpointRouteBuilder projects) => projects.MapPost("", Handle).WithIdempotency<CreateProjectRequest>();
                    private static string Handle(CreateProjectRequest request) => "created";
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Notes/CreateNote/CreateNoteEndpoint.cs", 8),
                ("backend/Features/Notes/CreateNote/CreateNoteEndpoint.cs", 9),
                ("backend/Features/Projects/CreateProject/CreateProjectEndpoint.cs", 8),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", 6),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("stubs/AspNetCore.cs", AspNetStub) },
            AnalyzerHost.Only<IdempotentPostAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0016")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
