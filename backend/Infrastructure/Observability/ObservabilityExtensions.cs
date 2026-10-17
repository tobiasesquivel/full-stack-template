using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
namespace TemplateApp.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddTemplateObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        string otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? configuration["Otel:Endpoint"]
            ?? "http://localhost:4318";

        services.AddMetrics();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("templateapp-api", serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = environment.EnvironmentName,
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql", "TemplateApp.Api")
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = new Uri($"{otlpEndpoint.TrimEnd('/')}/v1/traces");
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(
                    (exporter, reader) =>
                    {
                        exporter.Endpoint = new Uri($"{otlpEndpoint.TrimEnd('/')}/v1/metrics");
                        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                    }));

        return services;
    }
}
