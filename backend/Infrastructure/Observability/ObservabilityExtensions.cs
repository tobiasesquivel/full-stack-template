using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    private const string TracesExporter = "traces";
    private const string MetricsExporter = "metrics";

    public static IServiceCollection AddTemplateObservability(
        this IServiceCollection services,
        IHostEnvironment environment,
        string serviceName)
    {
        services.AddOptionsWithValidateOnStart<TelemetryOptions, TelemetryOptionsValidator>()
            .BindConfiguration(TelemetryOptions.Section)
            .BindConfiguration(string.Empty);
        services.AddOptions<OtlpExporterOptions>(TracesExporter)
            .Configure<IOptions<TelemetryOptions>>((exporter, telemetry) =>
                ConfigureExporter(exporter, telemetry.Value.SignalEndpoint("traces")));
        services.AddOptions<OtlpExporterOptions>(MetricsExporter)
            .Configure<IOptions<TelemetryOptions>>((exporter, telemetry) =>
                ConfigureExporter(exporter, telemetry.Value.SignalEndpoint("metrics")));

        services.AddSerilog((provider, logger) => ConfigureLogger(
            logger,
            provider.GetRequiredService<IOptions<TelemetryOptions>>().Value,
            environment.EnvironmentName),
            preserveStaticLogger: true);

        services.AddMetrics();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = environment.EnvironmentName,
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql", "TemplateApp.Api", OutboxTracing.SourceName, "Wolverine")
                .AddOtlpExporter(TracesExporter, configure: null))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("TemplateApp.*", "Wolverine*", "Polly")
                .AddOtlpExporter(
                    MetricsExporter,
                    (_, reader) => reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000));

        return services;
    }

    public static LoggerConfiguration ConfigureBootstrapLogger(LoggerConfiguration logger) =>
        ConfigureLevels(logger).WriteTo.Console();

    private static void ConfigureLogger(LoggerConfiguration logger, TelemetryOptions telemetry, string environmentName) =>
        ConfigureLevels(logger)
            .Enrich.With<ActivityTraceEnricher>()
            .WriteTo.Console()
            .WriteTo.OpenTelemetry(
                options =>
                {
                    options.Endpoint = telemetry.EffectiveEndpoint;
                    options.Protocol = OtlpProtocol.HttpProtobuf;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = "templateapp-api",
                        ["service.version"] = "1.0.0",
                        ["deployment.environment.name"] = environmentName,
                    };
                    options.IncludedData = IncludedData.MessageTemplateTextAttribute
                        | IncludedData.TraceIdField
                        | IncludedData.SpanIdField
                        | IncludedData.SpecRequiredResourceAttributes;
                },
                ignoreEnvironment: true);

    private static LoggerConfiguration ConfigureLevels(LoggerConfiguration logger) =>
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext();

    private static void ConfigureExporter(OtlpExporterOptions exporter, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
