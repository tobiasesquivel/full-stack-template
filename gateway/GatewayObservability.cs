using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace TemplateApp.Gateway;

internal static class GatewayObservability
{
    private const string ServiceName = "templateapp-gateway";
    private const string ServiceVersion = "1.0.0";
    private const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string DefaultEndpoint = "http://localhost:4318";

    public static IServiceCollection AddGatewayObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        string endpoint = (configuration[EndpointVariable] ?? DefaultEndpoint).TrimEnd('/');

        services.AddSerilog(
            (_, logger) => ConfigureLogger(logger, endpoint, environment.EnvironmentName),
            preserveStaticLogger: true);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(ServiceName, serviceVersion: ServiceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = environment.EnvironmentName,
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, "traces")))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter((exporter, reader) =>
                {
                    ConfigureExporter(exporter, endpoint, "metrics");
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                }));

        return services;
    }

    public static LoggerConfiguration ConfigureBootstrapLogger(LoggerConfiguration logger) =>
        ConfigureLevels(logger).WriteTo.Console();

    private static void ConfigureLogger(LoggerConfiguration logger, string endpoint, string environmentName) =>
        ConfigureLevels(logger)
            .WriteTo.Console()
            .WriteTo.OpenTelemetry(
                options =>
                {
                    options.Endpoint = endpoint;
                    options.Protocol = OtlpProtocol.HttpProtobuf;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = ServiceName,
                        ["service.version"] = ServiceVersion,
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
            .MinimumLevel.Override("Yarp", LogEventLevel.Warning)
            .Enrich.FromLogContext();

    private static void ConfigureExporter(OtlpExporterOptions exporter, string endpoint, string signal)
    {
        exporter.Endpoint = new Uri($"{endpoint}/v1/{signal}");
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
