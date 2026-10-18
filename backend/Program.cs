using Serilog;
using Serilog.Sinks.OpenTelemetry;
using TemplateApp.Features.Auth;
using TemplateApp.Features.Health;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Users;
using TemplateApp.Infrastructure.DependencyInjection;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Observability;
using TemplateApp.Infrastructure.Persistence;

string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4318";
string environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.With<ActivityTraceEnricher>()
    .WriteTo.Console()
    .WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = otlpEndpoint;
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
    })
    .CreateLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddBackendInfrastructure(builder.Configuration);
    builder.Services.AddTemplateObservability(builder.Configuration, builder.Environment);
    builder.Services.AddAuthFeature(builder.Configuration);
    builder.Services.AddUsersFeature();
    builder.Services.AddTasksFeature();
    builder.Services.AddHealthFeature(builder.Configuration);
    builder.Services.AddVersionedApi();

    WebApplication app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().WithDocumentPerVersion();
    }

    await app.InitializeDatabaseAsync();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAuthAntiforgery();

    RouteGroupBuilder api = app.MapVersionedApi();
    api.MapAuthEndpoints();
    api.MapUsersEndpoints();
    api.MapTasksEndpoints();
    api.MapHealthEndpoints();

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly during startup");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
public partial class Program;
