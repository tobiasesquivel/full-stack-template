using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class ConfigurationAccessAnalyzerTests
{
    [Fact]
    public async Task OptionsBoundByPathAreAccepted()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/Observability/TelemetryOptions.cs", """
                using Microsoft.Extensions.Configuration;
                namespace TemplateApp.Infrastructure.Observability;
                public sealed class TelemetryOptions
                {
                    public string Endpoint { get; set; } = "http://localhost:4318";
                    [ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")]
                    public string? StandardEndpoint { get; set; }
                }
                """),
            ("backend/Infrastructure/Observability/ObservabilityExtensions.cs", """
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Options;
                namespace TemplateApp.Infrastructure.Observability;
                public static class ObservabilityExtensions
                {
                    public static IServiceCollection AddTelemetry(this IServiceCollection services)
                    {
                        services.AddOptions<TelemetryOptions>()
                            .BindConfiguration("Otel")
                            .BindConfiguration(string.Empty);
                        services.AddOptions<Exporter>()
                            .Configure<IOptions<TelemetryOptions>>((exporter, telemetry) => exporter.Url = telemetry.Value.Endpoint);
                        return services;
                    }
                }
                public sealed class Exporter
                {
                    public string Url { get; set; } = string.Empty;
                }
                """),
            ("backend/Infrastructure/Persistence/Database.cs", """
                namespace TemplateApp.Infrastructure.Persistence;
                public sealed class Database
                {
                    public string? GetConnectionString() => null;
                    public string? Current() => GetConnectionString();
                }
                """),
            ("backend/Tests/Support/PostgresFixture.cs", """
                namespace TemplateApp.Tests.Support;
                public static class PostgresFixture
                {
                    public static string? External() => System.Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
                }
                """));

        Assert.Empty(Lines(diagnostics));
    }

    [Fact]
    public async Task LooseConfigurationReadsAreRejected()
    {
        var diagnostics = await AnalyzeAsync(
            ("backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs", """
                using System;
                using Microsoft.Extensions.Configuration;
                using Microsoft.Extensions.DependencyInjection;
                namespace TemplateApp.Infrastructure.DependencyInjection;
                public static class BackendInfrastructure
                {
                    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
                    {
                        _ = configuration["DataProtection:KeysDirectory"];
                        _ = configuration.GetValue<int>("X");
                        _ = configuration.GetConnectionString("Default");
                        _ = configuration.GetSection("Otel");
                        _ = new ConfigurationManager()["x"];
                        services.AddOptions<Exporter>().Configure<IConfiguration>((exporter, config) => exporter.Url = "x");
                        _ = Environment.GetEnvironmentVariable("X");
                        return services;
                    }
                }
                public sealed class Exporter
                {
                    public string Url { get; set; } = string.Empty;
                }
                """));

        Assert.Equal(new[] { 7, 9, 10, 11, 12, 13, 14, 15 }, Lines(diagnostics));
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        params (string Path, string Source)[] files) =>
        AnalyzerHost.AnalyzeAsync(
            new List<(string, string)>(files),
            AnalyzerHost.Only<ConfigurationAccessAnalyzer>());

    private static int[] Lines(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic => diagnostic.Id == "DUNE0013")
            .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)
            .Distinct()
            .OrderBy(line => line)
            .ToArray();
}
