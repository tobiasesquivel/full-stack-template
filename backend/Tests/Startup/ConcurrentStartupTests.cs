using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Infrastructure.Persistence.Initialization;
using Testcontainers.PostgreSql;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Startup;

public sealed class ConcurrentStartupTests : IAsyncLifetime
{
    private const int Instances = 8;
    private const int Rounds = 5;

    private PostgreSqlContainer? _postgres;
    private string _server = string.Empty;
    private string _connection = string.Empty;
    private WebApplicationFactory<MonolithHost>? _factory;

    public async ValueTask InitializeAsync()
    {
        string? external = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        string server;
        if (!string.IsNullOrWhiteSpace(external))
        {
            server = external;
        }
        else
        {
            _postgres = new PostgreSqlBuilder("postgres:18")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await _postgres.StartAsync();
            server = _postgres.GetConnectionString();
        }

        _server = server;
        _connection = await TestDatabases.CreateEmptyAsync(server);
        _factory = ApiFixture.CreateFactory(_connection);
        _ = _factory.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Initializers_RunningTogether_OnUnseededDatabase_AllSucceed_And_SeedEachPermissionOnce()
    {
        IServiceProvider services = _factory!.Services;

        for (int round = 0; round < Rounds; round++)
        {
            await DeleteRolesAsync();

            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task[] initializers = Enumerable.Range(0, Instances)
                .Select(_ => Task.Run(async () =>
                {
                    await start.Task;
                    await DatabaseInitializer.InitializeAsync(services);
                }))
                .ToArray();
            start.SetResult();

            await Task.WhenAll(initializers);
            await AssertEachRoleHasExactlyItsPermissionsAsync(services);
        }
    }

    [Fact]
    public async Task Initializers_RunningTogether_OnEmptyDatabase_ApplyEveryMigration()
    {
        string empty = await TestDatabases.CreateEmptyAsync(_server);
        using WebApplicationFactory<MonolithHost> factory = _factory!.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:DefaultConnection", empty)
            .ConfigureTestServices(TestDatabases.SkipInitialization));
        IServiceProvider services = factory.Services;

        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] initializers = Enumerable.Range(0, Instances)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                await DatabaseInitializer.InitializeAsync(services);
            }))
            .ToArray();
        start.SetResult();
        await Task.WhenAll(initializers);

        using IServiceScope scope = services.CreateScope();
        foreach (DbContext context in scope.ServiceProvider.MigratedContexts())
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    private async Task DeleteRolesAsync()
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(_connection);
        await using NpgsqlCommand delete = new("DELETE FROM \"AspNetRoleClaims\"; DELETE FROM \"AspNetRoles\";", connection);
        await delete.ExecuteNonQueryAsync();
    }

    private static async Task AssertEachRoleHasExactlyItsPermissionsAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        RoleManager<IdentityRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (string roleName in AppRoles.GetAll())
        {
            IdentityRole? role = await roles.FindByNameAsync(roleName);
            Assert.NotNull(role);

            IList<Claim> claims = await roles.GetClaimsAsync(role);
            string[] permissions = claims
                .Where(claim => claim.Type == RolePermissions.ClaimType)
                .Select(claim => claim.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(RolePermissions.GetForRole(roleName).Order(StringComparer.Ordinal), permissions);
        }
    }
}
