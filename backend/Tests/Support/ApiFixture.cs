using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Infrastructure.Seeders;
using Xunit;

namespace TemplateApp.Tests.Support;

public sealed class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private PostgreSqlContainer? _postgres;
    private string? _externalConnection;

    public HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    public IServiceProvider Services => _factory!.Services;

    public async ValueTask InitializeAsync()
    {
        _externalConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");

        string connectionString;
        if (!string.IsNullOrWhiteSpace(_externalConnection))
        {
            connectionString = _externalConnection;
        }
        else
        {
            _postgres = new PostgreSqlBuilder("postgres:18")
                .WithDatabase("templateapp_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        string jwtKey = "test-jwt-key-must-be-at-least-32-chars-long!!";

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Jwt:Key", jwtKey);
            builder.UseSetting("Jwt:Issuer", "https://tests.templateapp.local");
            builder.UseSetting("Jwt:Audience", "api://tests");
            builder.UseSetting("Jwt:AccessTokenExpirationMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpirationDays", "7");
        });

        using IServiceScope scope = Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        await RoleSeeder.SeedRolesAsync(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
