using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class HttpClientBoundaryAnalyzerTests
{
    private const string Packages = """
        using System;
        using System.Net.Http;
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }
            public static class HttpClientFactoryServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) where TClient : class => null!;
                public static IServiceCollection ConfigureHttpClientDefaults(this IServiceCollection services, Action<IHttpClientBuilder> configure) => services;
            }
            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder ConfigurePrimaryHttpMessageHandler(this IHttpClientBuilder builder, Func<HttpMessageHandler> configure) => builder;
            }
            public static class ResilienceHttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
        }
        namespace System.Net.Http
        {
            public interface IHttpClientFactory { HttpClient CreateClient(string name); }
        }
        namespace Polly.CircuitBreaker
        {
            public sealed class BrokenCircuitException : Exception { }
        }
        namespace TemplateApp.Infrastructure.Http
        {
            using Microsoft.Extensions.DependencyInjection;
            public enum RemoteRetry { Idempotent, Never }
            public sealed class RemoteServiceUnavailableException : Exception { }
            public static class RemoteServices
            {
                public static IHttpClientBuilder AddRemoteClient<TClient>(this IServiceCollection services, string service, RemoteRetry retry) where TClient : class =>
                    services.AddHttpClient<TClient>().AddStandardResilienceHandler();
            }
        }
        """;

    [Fact]
    public async Task InfrastructureHttpBuildsTheClients_AndAFeatureRegistersThemWithAddRemoteClient()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Http/RemoteServices.cs", """
                using System.Net.Http;
                using Microsoft.Extensions.DependencyInjection;
                using Polly.CircuitBreaker;
                namespace TemplateApp.Infrastructure.Http;
                public static class RemoteHandlers
                {
                    public static IHttpClientBuilder Route(IHttpClientBuilder client) =>
                        client.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler()).AddStandardResilienceHandler();
                    public static bool IsOpen(System.Exception ex) => ex is BrokenCircuitException;
                }
                """),
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Microsoft.Extensions.DependencyInjection;
                using TemplateApp.Infrastructure.Http;
                namespace TemplateApp.Features.Tasks;
                internal sealed class ProjectsClient { public ProjectsClient(System.Net.Http.HttpClient http) { } }
                internal static class TasksFeature
                {
                    public static void Add(IServiceCollection services) =>
                        services.AddRemoteClient<ProjectsClient>("Monolith", RemoteRetry.Idempotent);
                }
                """),
            ("backend/Tests/Remote/FakeServer.cs", """
                using System.Net.Http;
                namespace TemplateApp.Tests.Remote;
                internal static class FakeServer
                {
                    public static HttpClient Client() => new HttpClient(new HttpClientHandler());
                }
                """));

        Assert.Empty(Locations(diagnostics));
    }

    [Fact]
    public async Task AFeatureThatBuildsItsOwnClient_OrNamesPolly_IsAnError()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Features/Tasks/TasksFeature.cs", """
                using Microsoft.Extensions.DependencyInjection;
                namespace TemplateApp.Features.Tasks;
                internal sealed class ProjectsClient { public ProjectsClient(System.Net.Http.HttpClient http) { } }
                internal static class TasksFeature
                {
                    public static void Add(IServiceCollection services) =>
                        services.AddHttpClient<ProjectsClient>();
                }
                """),
            ("backend/Features/Tasks/CreateTask/ProjectsLookup.cs", """
                using System.Net.Http;
                namespace TemplateApp.Features.Tasks.CreateTask;
                internal static class ProjectsLookup
                {
                    public static HttpClient Client() => new HttpClient(new HttpClientHandler());
                }
                """),
            ("backend/Features/Tasks/Access/AuthSessionClient.cs", """
                using Polly.CircuitBreaker;
                namespace TemplateApp.Features.Tasks.Access;
                internal static class AuthSessionClient
                {
                    public static bool IsOpen(System.Exception ex) => ex is BrokenCircuitException;
                }
                """),
            ("backend/Features/Projects/RebuildTaskCounters/SnapshotReader.cs", """
                using System.Net.Http;
                namespace TemplateApp.Features.Projects.RebuildTaskCounters;
                internal sealed class SnapshotReader(IHttpClientFactory clients)
                {
                    public HttpClient Client() => clients.CreateClient("Tasks");
                }
                """),
            ("backend/Hosts/Tasks/Program.cs", """
                using Microsoft.Extensions.DependencyInjection;
                namespace TemplateApp.Hosts.Tasks;
                internal static class Startup
                {
                    public static void Add(IServiceCollection services) =>
                        services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler());
                }
                """));

        Assert.Equal(
            new[]
            {
                ("backend/Features/Projects/RebuildTaskCounters/SnapshotReader.cs", 3),
                ("backend/Features/Tasks/Access/AuthSessionClient.cs", 1),
                ("backend/Features/Tasks/Access/AuthSessionClient.cs", 5),
                ("backend/Features/Tasks/CreateTask/ProjectsLookup.cs", 5),
                ("backend/Features/Tasks/CreateTask/ProjectsLookup.cs", 5),
                ("backend/Features/Tasks/TasksFeature.cs", 7),
                ("backend/Hosts/Tasks/Program.cs", 6),
                ("backend/Hosts/Tasks/Program.cs", 6),
            },
            Locations(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files) { ("packages/HttpPackages.cs", Packages) },
            AnalyzerHost.Only<HttpClientBoundaryAnalyzer>());

    private static (string Path, int Line)[] Locations(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0027")
            .Select(diagnostic => (
                diagnostic.Location.SourceTree!.FilePath,
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(location => location.FilePath, System.StringComparer.Ordinal)
            .ThenBy(location => location.Item2)
            .ToArray();
}
