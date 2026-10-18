using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class AntiforgeryOptOutBanTests
{
    private const string AspNetStub = """
        namespace Microsoft.AspNetCore.Builder
        {
            public interface IApplicationBuilder { }
            public interface IEndpointConventionBuilder { }
            public sealed class RouteHandlerBuilder : IEndpointConventionBuilder { }
            public static class AntiforgeryApplicationBuilderExtensions
            {
                public static IApplicationBuilder UseAntiforgery(this IApplicationBuilder builder) => builder;
            }
            public static class RoutingEndpointConventionBuilderExtensions
            {
                public static TBuilder DisableAntiforgery<TBuilder>(this TBuilder builder)
                    where TBuilder : IEndpointConventionBuilder => builder;
            }
        }
        namespace Microsoft.AspNetCore.Antiforgery
        {
            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class RequireAntiforgeryTokenAttribute : System.Attribute
            {
                public RequireAntiforgeryTokenAttribute() { }
                public RequireAntiforgeryTokenAttribute(bool required) { }
            }
        }
        """;

    [Fact]
    public async Task EveryWayToSkipTheAntiforgeryCheckIsBannedInTheRealList()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            RealBannedSymbols(),
            new List<(string, string)>
            {
                ("stubs/AspNetCore.cs", AspNetStub),
                ("backend/Features/Webhooks/ReceiveWebhook/ReceiveWebhookEndpoint.cs", """
                    using Microsoft.AspNetCore.Antiforgery;
                    using Microsoft.AspNetCore.Builder;
                    namespace TemplateApp.Features.Webhooks.ReceiveWebhook;
                    public static class ReceiveWebhookEndpoint
                    {
                        public static void Map(RouteHandlerBuilder route, IApplicationBuilder app)
                        {
                            route.DisableAntiforgery();
                            app.UseAntiforgery();
                        }

                        [RequireAntiforgeryToken(false)]
                        public static string Handle() => "skipped";
                    }
                    """),
            });

        var lines = diagnostics
            .Where(diagnostic => diagnostic.Id == "RS0030")
            .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
            .Distinct()
            .OrderBy(line => line)
            .ToArray();
        Assert.Equal(new[] { 8, 9, 12 }, lines);
    }

    [Fact]
    public async Task RequiringTheTokenStaysAllowed()
    {
        var diagnostics = await BannedApiHost.AnalyzeAsync(
            RealBannedSymbols(),
            new List<(string, string)>
            {
                ("stubs/AspNetCore.cs", AspNetStub),
                ("backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs", """
                    using Microsoft.AspNetCore.Antiforgery;
                    namespace TemplateApp.Features.Tasks.CreateTask;
                    public static class CreateTaskEndpoint
                    {
                        [RequireAntiforgeryToken]
                        public static string Handle() => "created";
                    }
                    """),
            });

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "RS0030");
    }

    private static string RealBannedSymbols()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "dune", "BannedSymbols.txt");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new InvalidOperationException("dune/BannedSymbols.txt not found above the test output.");
    }
}
