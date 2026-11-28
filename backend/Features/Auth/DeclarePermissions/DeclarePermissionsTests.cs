using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Auth.DeclarePermissions;

public sealed class DeclarePermissionsTests : IClassFixture<ApiFixture>
{
    private const string Route = "internal/v1/feature-permissions/";

    private readonly ApiFixture _fixture;
    private readonly string _server;

    public DeclarePermissionsTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _server = postgres.ConnectionString;
    }

    [Theory]
    [InlineData("Tasks", "Permissions.Users.Manage", "User", "a feature declares only its own permissions")]
    [InlineData("Tasks", "Permissions.Tasks.read", "User", "a feature declares only its own permissions")]
    [InlineData("Tasks", "Permissions.Tasks.Read", "Owner", "names the unknown role 'Owner'")]
    [InlineData("tasks", "Permissions.tasks.Read", "User", "must be a PascalCase name")]
    [InlineData("Users", "Permissions.Users.Manage", "User", "runs in this process")]
    [InlineData("Health", "Permissions.Health.Read", "User", "runs in this process")]
    public async Task AnInvalidDeclaration_Answers400_AndChangesNothing(string feature, string permission, string role, string problem)
    {
        using HttpClient monolith = _fixture.CreateMonolithClient();
        List<string> before = await GrantsAsync(_fixture.Services);

        using HttpResponseMessage response = await DeclareAsync(monolith, feature, new { permission, roles = new[] { role } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(before, await GrantsAsync(_fixture.Services));
    }

    [Fact]
    public async Task ADeclarationTwice_Answers204_AndGrantsEachPermissionOnce()
    {
        using ApiPair api = await FreshApiAsync();
        using HttpClient monolith = api.CreateMonolithClient();

        using (HttpResponseMessage first = await DeclareAsync(monolith, "Probe", new { permission = "Permissions.Probe.Read", roles = new[] { "User", "Admin" } }))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using (HttpResponseMessage second = await DeclareAsync(monolith, "Probe", new { permission = "Permissions.Probe.Read", roles = new[] { "User", "Admin" } }))
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        Assert.Equal(["Admin:Permissions.Probe.Read", "User:Permissions.Probe.Read"], await GrantsAsync(api.Monolith.Services, "Permissions.Probe."));
    }

    [Fact]
    public async Task ADeclaration_OnlyAdds_AndLeavesTheOtherFeaturesAlone()
    {
        using ApiPair api = await FreshApiAsync();
        using HttpClient monolith = api.CreateMonolithClient();
        List<string> others = await GrantsAsync(api.Monolith.Services);

        using (HttpResponseMessage first = await DeclareAsync(monolith, "Probe", new { permission = "Permissions.Probe.Read", roles = new[] { "Admin" } }))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using (HttpResponseMessage wider = await DeclareAsync(monolith, "Probe", new { permission = "Permissions.Probe.Read", roles = new[] { "User", "Admin" } }))
            Assert.Equal(HttpStatusCode.NoContent, wider.StatusCode);
        Assert.Equal(["Admin:Permissions.Probe.Read", "User:Permissions.Probe.Read"], await GrantsAsync(api.Monolith.Services, "Permissions.Probe."));

        using (HttpResponseMessage older = await DeclareAsync(monolith, "Probe"))
            Assert.Equal(HttpStatusCode.NoContent, older.StatusCode);
        Assert.Equal(["Admin:Permissions.Probe.Read", "User:Permissions.Probe.Read"], await GrantsAsync(api.Monolith.Services, "Permissions.Probe."));
        List<string> after = await GrantsAsync(api.Monolith.Services);
        Assert.Equal(others, after.Where(grant => !grant.Contains(":Permissions.Probe.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DeclarationsRunningTogether_GrantEachPermissionOnce()
    {
        using ApiPair api = await FreshApiAsync();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpStatusCode>[] declarations = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                using HttpClient monolith = api.CreateMonolithClient();
                await start.Task;
                using HttpResponseMessage response = await DeclareAsync(monolith, "Probe", new { permission = "Permissions.Probe.Read", roles = new[] { "User", "Admin", "SuperAdmin" } });
                return response.StatusCode;
            }))
            .ToArray();
        start.SetResult();

        Assert.All(await Task.WhenAll(declarations), status => Assert.Equal(HttpStatusCode.NoContent, status));
        Assert.Equal(
            ["Admin:Permissions.Probe.Read", "SuperAdmin:Permissions.Probe.Read", "User:Permissions.Probe.Read"],
            await GrantsAsync(api.Monolith.Services, "Permissions.Probe."));
    }

    private async Task<ApiPair> FreshApiAsync()
    {
        ApiPair api = new(await TestDatabases.CreateEmptyAsync(_server), new Dictionary<string, string>(), _ => { });
        api.Start();
        return api;
    }

    private static Task<HttpResponseMessage> DeclareAsync(HttpClient monolith, string feature, params object[] grants) =>
        monolith.PutAsJsonAsync(Route + feature, new { grants }, TestContext.Current.CancellationToken);

    private static async Task<List<string>> GrantsAsync(IServiceProvider services, string prefix = "")
    {
        using IServiceScope scope = services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        string pattern = prefix + "%";
        return await db.Database
            .SqlQuery<string>($"""
                SELECT r."Name" || ':' || c."ClaimValue" AS "Value"
                FROM auth."AspNetRoleClaims" c JOIN auth."AspNetRoles" r ON r."Id" = c."RoleId"
                WHERE c."ClaimType" = 'permission' AND c."ClaimValue" LIKE {pattern}
                ORDER BY 1
                """)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
