using JasperFx;
using Scalar.AspNetCore;
using Serilog;
using TemplateApp.Infrastructure.DependencyInjection;
using TemplateApp.Infrastructure.Seeders;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("JasperFx.CodeGeneration", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Wolverine", Serilog.Events.LogEventLevel.Information)
    .Filter.ByExcluding(logEvent => logEvent.Exception is FluentValidation.ValidationException)
    .WriteTo.Console()
    .CreateLogger();
try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    builder.Services.AddInfrastructure(builder.Configuration, builder.Host, builder.Environment);
    builder.Services.AddOpenApi();

    WebApplication app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseHttpsRedirection();
    app.UseExceptionHandler();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    await app.SeedDatabaseAsync();

    await app.RunJasperFxCommands(args);

}
catch (HostAbortedException)
{
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
