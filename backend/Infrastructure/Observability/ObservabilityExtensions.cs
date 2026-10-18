using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
namespace TemplateApp.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    private const string TracesExporter = "traces";
    private const string MetricsExporter = "metrics";

    public static IServiceCollection AddTemplateObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.Section))
            .Configure(options => options.Endpoint = configuration[TelemetryOptions.EndpointVariable] ?? options.Endpoint)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();
        services.AddOptions<OtlpExporterOptions>(TracesExporter)
            .Configure<IOptions<TelemetryOptions>>((exporter, telemetry) =>
                ConfigureExporter(exporter, telemetry.Value.SignalEndpoint("traces")));
        services.AddOptions<OtlpExporterOptions>(MetricsExporter)
            .Configure<IOptions<TelemetryOptions>>((exporter, telemetry) =>
                ConfigureExporter(exporter, telemetry.Value.SignalEndpoint("metrics")));

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
                .AddOtlpExporter(TracesExporter, configure: null))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(
                    MetricsExporter,
                    (_, reader) => reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000));

        return services;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
