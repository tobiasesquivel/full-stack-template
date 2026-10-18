using Serilog;
using TemplateApp.Features.Auth;
using TemplateApp.Features.Health;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Users;
using TemplateApp.Infrastructure.DependencyInjection;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Observability;

Log.Logger = ObservabilityExtensions.ConfigureBootstrapLogger(new LoggerConfiguration()).CreateLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Services.AddBackendInfrastructure();
    builder.Services.AddTemplateObservability(builder.Environment);
    builder.Services.AddAuthFeature();
    builder.Services.AddUsersFeature();
    builder.Services.AddTasksFeature();
    builder.Services.AddHealthFeature();
    builder.Services.AddVersionedApi();

    WebApplication app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().WithDocumentPerVersion();
    }

    app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
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
