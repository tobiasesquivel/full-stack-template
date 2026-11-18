using Serilog;
using TemplateApp.Features.Health;
using TemplateApp.Features.Tasks;
using TemplateApp.Infrastructure.DependencyInjection;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Observability;

Log.Logger = ObservabilityExtensions.ConfigureBootstrapLogger(new LoggerConfiguration()).CreateLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Services.AddBackendInfrastructure(migratesPlatformSchema: false);
    builder.Services.AddTemplateObservability(builder.Environment, "templateapp-tasks");
    builder.Services.AddTasksFeature();
    builder.Services.AddHealthFeature();
    builder.Services.AddVersionedApi();
    builder.Services.AddApiRateLimiting();
    builder.Services.AddApiSecurityHeaders();

    WebApplication app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().WithDocumentPerVersion();
    }

    app.UseApiSecurityHeaders();
    app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.UseSessionAntiforgery();

    RouteGroupBuilder api = app.MapVersionedApi();
    api.MapTasksEndpoints();
    api.MapHealthEndpoints();

    app.MapInternalApi()
        .MapTasksInternalEndpoints();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly during startup");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
