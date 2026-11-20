using System.Diagnostics;
using Xunit;

namespace TemplateApp.Tests.Startup;

public sealed class ConfigurationValidationTests : IDisposable
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=never;Username=postgres;Password=postgres;Timeout=2";
    private const string StartupFailure = "Application terminated unexpectedly during startup";

    private readonly string _contentRoot = Directory.CreateTempSubdirectory("templateapp-config-").FullName;

    [Theory]
    [InlineData("Session__AbsoluteExpirationDays", "0", "SessionOptions.AbsoluteExpirationDays must be between 1 and 365.")]
    [InlineData("Session__IdleTimeoutMinutes", "2000", "SessionOptions.IdleTimeoutMinutes must be between 1 and 1440.")]
    [InlineData("RateLimiting__UserWrites", "0", "ApiRateLimitOptions.UserWrites must be between 1 and 100000.")]
    [InlineData("AuthRateLimiting__WindowSeconds", "0", "AuthRateLimitOptions.WindowSeconds must be between 1 and 86400.")]
    [InlineData("ConnectionStrings__DefaultConnection", null, "ConnectionStrings:DefaultConnection is required.")]
    [InlineData("ConnectionStrings__DefaultConnection", "postgres://localhost/app", "ConnectionStrings:DefaultConnection is not a valid PostgreSQL connection string.")]
    [InlineData("ConnectionStrings__DefaultConnection", "Database=app;Username=postgres", "ConnectionStrings:DefaultConnection must set Host.")]
    [InlineData("DataProtection__KeysDirectory", "relative/keys", "DataProtection:KeysDirectory must be an absolute path.")]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT", "collector:4318", "Otel:Endpoint (or OTEL_EXPORTER_OTLP_ENDPOINT) must be an absolute http or https URL.")]
    [InlineData("Otel__Endpoint", "localhost:4318", "Otel:Endpoint (or OTEL_EXPORTER_OTLP_ENDPOINT) must be an absolute http or https URL.")]
    [InlineData("Services__Tasks__BaseAddress", null, "Services:Tasks:BaseAddress must be an absolute http or https URL.")]
    [InlineData("Services__Tasks__BaseAddress", "tasks:8080", "Services:Tasks:BaseAddress must be an absolute http or https URL.")]
    [InlineData("Services__Tasks__Timeout", "00:01:00", "Services:Tasks:Timeout must be between 10 ms and 15 seconds.")]
    [InlineData("Services__Tasks__Timeout", "00:00:00.005", "Services:Tasks:Timeout must be between 10 ms and 15 seconds.")]
    [InlineData("Services__Tasks__RetryDelay", "2.00:00:00", "Services:Tasks:RetryDelay must be greater than zero and shorter than TotalTimeout.")]
    [InlineData("Services__Tasks__TotalTimeout", "00:00:01", "Services:Tasks:TotalTimeout must be greater than Timeout and at most 30 seconds.")]
    [InlineData("Services__Tasks__BreakerMinimumThroughput", "1", "Services:Tasks:BreakerMinimumThroughput must be at least 2.")]
    [InlineData("Messaging__RabbitMqUri", null, "Messaging:RabbitMqUri must be an absolute amqp or amqps URL.")]
    [InlineData("Messaging__RabbitMqUri", "http://rabbitmq:15672/", "Messaging:RabbitMqUri must be an absolute amqp or amqps URL.")]
    [InlineData("Messaging__Prefix", "Tests_1", "Messaging:Prefix may only use lowercase letters, digits and hyphens.")]
    public async Task Api_WithAnInvalidSetting_ExitsBeforeTouchingTheDatabase_NamingTheSetting(string variable, string? value, string expected)
    {
        (int exitCode, string output) = await RunApiAsync(variable, value);

        Assert.Equal(1, exitCode);
        Assert.Contains("OptionsValidationException: ", output, StringComparison.Ordinal);
        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.Contains(StartupFailure, output, StringComparison.Ordinal);
        Assert.DoesNotContain("NpgsqlException", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_WithAValidConfiguration_PassesValidation_AndReachesTheDatabase()
    {
        (int exitCode, string output) = await RunApiAsync("Otel__Endpoint", "https://collector.example:4318");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("OptionsValidationException", output, StringComparison.Ordinal);
        Assert.Contains("NpgsqlException", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Services:Monolith:BaseAddress must be an absolute http or https URL.")]
    [InlineData("ftp://monolith/", "Services:Monolith:BaseAddress must be an absolute http or https URL.")]
    public async Task Tasks_WithoutAValidMonolithAddress_ExitsBeforeTouchingTheDatabase_NamingTheSetting(string? value, string expected)
    {
        (int exitCode, string output) = await RunApiAsync("Services__Monolith__BaseAddress", value, "TemplateApp.Tasks.dll");

        Assert.Equal(1, exitCode);
        Assert.Contains("OptionsValidationException: ", output, StringComparison.Ordinal);
        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.Contains(StartupFailure, output, StringComparison.Ordinal);
        Assert.DoesNotContain("NpgsqlException", output, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    private async Task<(int ExitCode, string Output)> RunApiAsync(string variable, string? value, string assembly = "TemplateApp.dll")
    {
        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = _contentRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, assembly));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__DefaultConnection"] = UnreachableDatabase;
        start.Environment["Session__AbsoluteExpirationDays"] = "7";
        start.Environment["Session__IdleTimeoutMinutes"] = "30";
        start.Environment["Services__Tasks__BaseAddress"] = "http://tasks.invalid/";
        start.Environment["Services__Monolith__BaseAddress"] = "http://monolith.invalid/";
        start.Environment["Messaging__RabbitMqUri"] = "amqp://rabbitmq.invalid:5672/";
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (value is null)
            start.Environment.Remove(variable);
        else
            start.Environment[variable] = value;

        using Process api = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        Task<string> stdout = api.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = api.StandardError.ReadToEndAsync(timeout.Token);
        await api.WaitForExitAsync(timeout.Token);

        return (api.ExitCode, await stdout + await stderr);
    }
}
