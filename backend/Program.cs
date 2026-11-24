using Serilog;
using TemplateApp.Features.Auth;
using TemplateApp.Features.Health;
using TemplateApp.Features.Projects;
using TemplateApp.Features.Users;
using TemplateApp.Infrastructure;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Http.RateLimiting;
using TemplateApp.Infrastructure.Http.Remote;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Observability;

Log.Logger = ObservabilityExtensions.ConfigureBootstrapLogger(new LoggerConfiguration()).CreateLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Services.AddBackendInfrastructure(migratesPlatformSchema: true);
    builder.Services.AddTemplateObservability(builder.Environment, "templateapp-api");
    builder.Services.AddMessaging(typeof(TemplateApp.MonolithHost).Assembly, storesSagas: true);
    builder.Services.AddAuthFeature();
    builder.Services.AddUsersFeature();
    builder.Services.AddProjectsFeature();
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
    api.MapAuthEndpoints();
    api.MapUsersEndpoints();
    api.MapProjectsEndpoints();
    api.MapHealthEndpoints();

    app.MapInternalApi()
        .MapAuthInternalEndpoints()
        .MapProjectsInternalEndpoints();

    return await app.RunWithMessagingCommandsAsync(args);
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
public partial class Program;
