using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TemplateApp.Gateway;

Log.Logger = GatewayObservability.ConfigureBootstrapLogger(new LoggerConfiguration()).CreateLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Services.AddGatewayObservability(builder.Configuration, builder.Environment);
    builder.Services.AddGatewaySecurityHeaders();
    builder.Services.AddGatewayProblemDetails();
    builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
    builder.Services.AddHealthChecks().AddCheck<ProxyConfigHealthCheck>("proxy-config", tags: [GatewayHealth.ReadyTag]);

    WebApplication app = builder.Build();

    app.UseSecurityHeaders();
    app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = GatewayHealth.IsReadyCheck });
    app.MapReverseProxy();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Gateway terminated unexpectedly during startup");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
