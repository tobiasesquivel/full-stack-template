using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Hosts.Monolith;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Authorization;

public sealed class FeaturePermissionsTests : IClassFixture<ApiFixture>
{
    private static readonly TimeSpan DeclarationTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] UserPermissions =
    [
        "Permissions.Projects.Create",
        "Permissions.Tasks.Create",
    ];

    private static readonly string[] AdminPermissions =
    [
        "Permissions.Projects.Read",
        "Permissions.Projects.Update",
        "Permissions.Tasks.Create",
        "Permissions.Tasks.Delete",
        "Permissions.Tasks.Read",
        "Permissions.Tasks.Update",
        "Permissions.Users.Read",
    ];

    private static readonly string[] SuperAdminPermissions =
    [
        "Permissions.Projects.Create",
        "Permissions.Projects.Read",
        "Permissions.Projects.Update",
        "Permissions.Tasks.Create",
        "Permissions.Tasks.Delete",
        "Permissions.Tasks.Read",
        "Permissions.Tasks.Update",
        "Permissions.Users.Manage",
        "Permissions.Users.Read",
    ];

    private readonly ApiFixture _fixture;
    private readonly string _server;

    public FeaturePermissionsTests(ApiFixture fixture, PostgresFixture postgres)
    {
        _fixture = fixture;
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task EachRole_KeepsItsPermissions_InTheSessionCheckThatTasksUses()
    {
        string user = await RegisterWithOnlyRoleAsync(AppRoles.User);
        string admin = await RegisterWithOnlyRoleAsync(AppRoles.Admin);
        string superAdmin = await RegisterWithOnlyRoleAsync(AppRoles.SuperAdmin);

        Assert.Equal(UserPermissions, await TasksCheckAsync(_fixture.TasksServices, _fixture.Services, user));
        Assert.Equal(AdminPermissions, await TasksCheckAsync(_fixture.TasksServices, _fixture.Services, admin));
        Assert.Equal(SuperAdminPermissions, await TasksCheckAsync(_fixture.TasksServices, _fixture.Services, superAdmin));
    }

    [Fact]
    public async Task ChangingATasksPermission_InTheTasksProcess_ReachesTheSessionCheck_AndAnOlderTasksDoesNotTakeItBack()
    {
        string database = await TestDatabases.CreateEmptyAsync(_server);
        string authId;
        using (ApiPair before = Pair(database, configureTasks: null))
        {
            before.Start();
            TestSession alice = new(before.CreateClient());
            await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
            authId = alice.AuthId;
            Assert.Equal(UserPermissions, await TasksCheckAsync(before.Tasks.Services, before.Monolith.Services, authId));
        }

        FeaturePermissions changed = new("Tasks",
        [
            new(TasksPermissions.Create, [AppRoles.User, AppRoles.Admin, AppRoles.SuperAdmin]),
            new(TasksPermissions.Read, [AppRoles.User, AppRoles.Admin, AppRoles.SuperAdmin]),
            new(TasksPermissions.Update, [AppRoles.Admin, AppRoles.SuperAdmin]),
            new(TasksPermissions.Delete, [AppRoles.Admin, AppRoles.SuperAdmin]),
        ]);
        string[] withRead = ["Permissions.Projects.Create", "Permissions.Tasks.Create", "Permissions.Tasks.Read"];
        using (ApiPair after = Pair(database, services => services.AddSingleton(new HostPermissions([changed]))))
        {
            _ = after.Monolith.Services;
            Assert.Equal(UserPermissions, await MonolithCheckAsync(after, authId));

            _ = after.Tasks.Services;
            await after.TasksDeclared.WaitAsync(DeclarationTimeout, TestContext.Current.CancellationToken);
            Assert.Equal(withRead, await TasksCheckAsync(after.Tasks.Services, after.Monolith.Services, authId));
        }

        using ApiPair older = Pair(database, configureTasks: null);
        older.Start();
        Assert.Equal(withRead, await TasksCheckAsync(older.Tasks.Services, older.Monolith.Services, authId));
    }

    [Fact]
    public async Task ALocalDeclaration_WithAnotherFeaturesPermission_StopsTheSeeder_BeforeTouchingTheRoles()
    {
        string database = await TestDatabases.CreateEmptyAsync(_server);
        using WebApplicationFactory<MonolithHost> monolith = ApiFixture.CreateFactory(database);
        using IServiceScope scope = monolith.Services.CreateScope();
        await DeleteRolesAsync(database);
        PermissionCatalog catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();
        FeaturePermissions foreign = new("Users", [new(TasksPermissions.Read, [AppRoles.User])]);
        HostPermissions local = scope.ServiceProvider.GetRequiredService<HostPermissions>();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RoleSeeder(catalog, new HostPermissions([.. local.All, foreign])).SeedAsync(TestContext.Current.CancellationToken));

        Assert.Contains($"Permission '{TasksPermissions.Read}' must be Permissions.Users.<Action>", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(database, "SELECT count(*) FROM auth.\"AspNetRoles\""));
        Assert.Equal(0, await CountAsync(database, "SELECT count(*) FROM auth.\"AspNetRoleClaims\""));

        await new RoleSeeder(catalog, local).SeedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AppRoles.GetAll().Count, await CountAsync(database, "SELECT count(*) FROM auth.\"AspNetRoles\""));
    }

    [Fact]
    public async Task TheTasksHost_IsNotReady_UntilAuthAcceptsItsPermissions()
    {
        bool refuse = true;
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string>(),
            _ => { },
            (request, _) => refuse && ApiPair.IsPermissionsDeclaration(request) ? new Unavailable() : null,
            start: false);
        _ = api.Monolith.Services;
        _ = api.Tasks.Services;
        using HttpClient tasks = api.CreateTasksClient();

        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        using (HttpResponseMessage waiting = await tasks.GetAsync("/api/v1/health/ready", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, waiting.StatusCode);
            Assert.Equal("unhealthy", await CheckStatusAsync(waiting));
        }

        SessionResponse early = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        ProblemAssert.Problem(early, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");

        refuse = false;
        await api.TasksDeclared.WaitAsync(DeclarationTimeout, TestContext.Current.CancellationToken);
        SessionResponse served = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        served.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, served.Status);

        using HttpResponseMessage ready = await tasks.GetAsync("/api/v1/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("healthy", await CheckStatusAsync(ready));
    }

    [Fact]
    public async Task AMonolithWithoutTheDeclarationRoute_LetsTasksServe_WithThePermissionsThatReleaseSeeded()
    {
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string>(),
            _ => { },
            (request, _) => ApiPair.IsPermissionsDeclaration(request) ? new RouteMissing() : null,
            start: false);
        _ = api.Monolith.Services;
        _ = api.Tasks.Services;

        await api.TasksDeclared.WaitAsync(DeclarationTimeout, TestContext.Current.CancellationToken);

        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateTaskAsync("Con el monolito anterior");
        using HttpClient tasks = api.CreateTasksClient();
        using HttpResponseMessage ready = await tasks.GetAsync("/api/v1/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task OnKestrel_ADeclarationAnswered503Once_IsRetriedAgainstTheMonolith()
    {
        string database = await TestDatabases.CreateEmptyAsync(_server);
        ConcurrentQueue<(HttpRequestMessage Request, string Host)> sends = new();
        UnavailableAfterForwarding first = null!;
        using ApiPair api = new(
            database,
            new Dictionary<string, string>(),
            _ => { },
            (request, server) =>
            {
                if (!ApiPair.IsPermissionsDeclaration(request))
                    return null;

                sends.Enqueue((request, request.RequestUri!.Host));
                return sends.Count == 1 ? first = new UnavailableAfterForwarding(server) : null;
            },
            kestrel: true);

        api.Start();

        Assert.True(api.TasksDeclared.IsCompletedSuccessfully);
        Assert.Equal(HttpStatusCode.NoContent, first.Forwarded);
        (HttpRequestMessage Request, string Host)[] attempts = [.. sends];
        Assert.Equal(2, attempts.Length);
        Assert.Same(attempts[0].Request, attempts[1].Request);
        Assert.All(attempts, attempt => Assert.Equal(ApiPair.MonolithAuthority, attempt.Host));
    }

    private async Task<string> RegisterWithOnlyRoleAsync(string role)
    {
        string name = TestAccounts.UniqueName(role.ToLowerInvariant());
        TestSession session = new(_fixture.CreateClient());
        await session.RegisterAsync(name);
        if (role != AppRoles.User)
        {
            await _fixture.SetRoleAsync(name, role, member: true);
            await _fixture.SetRoleAsync(name, AppRoles.User, member: false);
        }

        return session.AuthId;
    }

    private static async Task DeleteRolesAsync(string database)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand delete = new("DELETE FROM auth.\"AspNetRoleClaims\"; DELETE FROM auth.\"AspNetRoles\";", connection);
        await delete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string database, string sql)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(database);
        await using NpgsqlCommand count = new(sql, connection);
        return (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static ApiPair Pair(string database, Action<IServiceCollection>? configureTasks) =>
        new(database, new Dictionary<string, string>(), _ => { }, configureTasks: configureTasks);

    private static async Task<IReadOnlyList<string>?> TasksCheckAsync(IServiceProvider tasks, IServiceProvider monolith, string authId)
    {
        Guid session = await SessionIdAsync(monolith, authId);
        using IServiceScope scope = tasks.CreateScope();
        AuthSessionClient auth = scope.ServiceProvider.GetRequiredService<AuthSessionClient>();
        return await auth.CheckAsync(authId, session, TestContext.Current.CancellationToken);
    }

    private static async Task<string[]> MonolithCheckAsync(ApiPair api, string authId)
    {
        Guid session = await SessionIdAsync(api.Monolith.Services, authId);
        using HttpClient monolith = api.CreateMonolithClient();
        using HttpResponseMessage response = await monolith.PostAsJsonAsync(
            "internal/v1/session-checks",
            new { userId = authId, sessionId = session },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();
    }

    private static async Task<Guid> SessionIdAsync(IServiceProvider monolith, string authId)
    {
        using IServiceScope scope = monolith.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM auth.\"Sessions\" WHERE \"UserId\" = {authId}")
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string> CheckStatusAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("checks").GetProperty(TasksFeature.PermissionsDeclarationCheck).GetString()!;
    }

    private sealed class UnavailableAfterForwarding(HttpMessageHandler server) : HttpMessageHandler
    {
        public HttpStatusCode? Forwarded { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using HttpMessageInvoker forward = new(server, disposeHandler: false);
            using HttpResponseMessage served = await forward.SendAsync(request, cancellationToken);
            Forwarded = served.StatusCode;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class RouteMissing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class Unavailable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
