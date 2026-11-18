using System.Diagnostics;
using System.Net;
using System.Reflection;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Tasks;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Remote;

public sealed class RemoteBoundaryTests : IClassFixture<ApiFixture>
{
    private const string SessionChecks = "/internal/v1/session-checks";
    private const string OwnerScoped = "/internal/v1/owners/";

    private readonly ApiFixture _fixture;

    public RemoteBoundaryTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RevokedSession_LosesTasksOnTheNextRequest()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateTaskAsync("Antes de cerrar");
        TestSession stolen = alice.CopyTo(_fixture.CreateClient());

        SessionResponse logout = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/logout");
        logout.Payload?.Dispose();
        SessionResponse after = await stolen.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);

        Assert.Equal(HttpStatusCode.NoContent, logout.Status);
        ProblemAssert.Problem(after, HttpStatusCode.Unauthorized, "Auth.SessionRequired");
        Assert.Contains(after.SetCookies, cookie => cookie.StartsWith(TestAccounts.SessionCookieName + "=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthUnreachable_TasksAnswers503_AndKeepsTheSession()
    {
        RemoteFault auth = RemoteFault.Unreachable(RemoteFault.Path(SessionChecks));
        auth.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(auth);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        auth.Active = true;
        SessionResponse during = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        auth.Active = false;
        SessionResponse after = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        after.Payload?.Dispose();

        Assert.Equal(1, auth.Calls);
        Assert.DoesNotContain(during.SetCookies, cookie => cookie.StartsWith(TestAccounts.SessionCookieName, StringComparison.Ordinal));
        ProblemAssert.Problem(during, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.Equal(HttpStatusCode.OK, after.Status);
    }

    [Fact]
    public async Task AuthWithoutTheSessionRoute_AsDuringARollingDeploy_TasksAnswers503_AndKeepsTheSession()
    {
        RemoteFault olderMonolith = RemoteFault.MissingRoute(RemoteFault.Path(SessionChecks));
        olderMonolith.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(olderMonolith);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        olderMonolith.Active = true;
        SessionResponse during = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        olderMonolith.Active = false;
        SessionResponse after = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        after.Payload?.Dispose();

        Assert.DoesNotContain(during.SetCookies, cookie => cookie.StartsWith(TestAccounts.SessionCookieName, StringComparison.Ordinal));
        ProblemAssert.Problem(during, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.Equal(HttpStatusCode.OK, after.Status);
    }

    [Fact]
    public async Task ProjectsWithoutTheAssignmentRoute_CreatingATaskInAProject_Answers503_InsteadOfRejectingTheProject()
    {
        RemoteFault olderMonolith = RemoteFault.MissingRoute(request =>
            RemoteFault.Host(ApiPair.MonolithAuthority)(request) && RemoteFault.Path(OwnerScoped)(request));
        olderMonolith.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(olderMonolith);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string project = await alice.CreateProjectAsync("Mudanza");

        olderMonolith.Active = true;
        SessionResponse inProject = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Embalar", projectId = project });

        ProblemAssert.Problem(inProject, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
    }

    [Fact]
    public async Task TasksAnswerLost_AfterDeletingTheTasks_TheAccountStaysWithoutTasks_AndTheRetryFinishes()
    {
        RemoteFault tasks = RemoteFault.LostResponse(RemoteFault.Host(ApiPair.TasksAuthority));
        tasks.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(tasks);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateTaskAsync("Se borra aunque se pierda la respuesta");
        await alice.CreateProjectAsync("Queda hasta el reintento");

        tasks.Active = true;
        SessionResponse during = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        OwnedRows afterFailure = await api.Monolith.Services.OwnedRowsAsync(alice.AuthId);
        tasks.Active = false;
        SessionResponse retry = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        retry.Payload?.Dispose();

        ProblemAssert.Problem(during, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.Equal(new OwnedRows(1, 1, 1, 1, 0, 1, 0), afterFailure);
        Assert.Equal(HttpStatusCode.NoContent, retry.Status);
        Assert.Equal(OwnedRows.None, await api.Monolith.Services.OwnedRowsAsync(alice.AuthId));
    }

    [Fact]
    public async Task ProjectsUnreachable_CreatingATaskInAProject_Answers503_AndSavesNothing()
    {
        RemoteFault projects = RemoteFault.Unreachable(request =>
            RemoteFault.Host(ApiPair.MonolithAuthority)(request) && RemoteFault.Path(OwnerScoped)(request));
        projects.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(projects);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string project = await alice.CreateProjectAsync("Mudanza");

        projects.Active = true;
        SessionResponse inProject = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/tasks",
            new { title = "Embalar", projectId = project },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        string loose = await alice.CreateTaskAsync("Sin proyecto");

        ProblemAssert.Problem(inProject, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.Equal(1, projects.Calls);
        Assert.Equal(new OwnedRows(1, 1, 1, 1, 1, 1, 0), await api.Monolith.Services.OwnedRowsAsync(alice.AuthId));
        Assert.False(string.IsNullOrEmpty(loose));
    }

    [Fact]
    public async Task TasksUnreachable_ProjectReadsAnswer503_ButANewProjectIsCreated()
    {
        RemoteFault tasks = RemoteFault.Unreachable(RemoteFault.Host(ApiPair.TasksAuthority));
        ApiPair api = _fixture.CreateIsolatedApi(tasks);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse created = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "Nuevo" });
        SessionResponse list = await alice.SendAsync(HttpMethod.Get, "api/v1/projects", csrf: false);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        using (created.Payload)
            Assert.Equal(0, created.Payload.RootElement.GetProperty("taskCount").GetInt32());
        ProblemAssert.Problem(list, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.Equal(1, tasks.Calls);
    }

    [Fact]
    public async Task TasksUnreachable_DeletingTheAccount_Answers503_LeavesEveryRow_AndTheRetryLeavesNoOrphan()
    {
        RemoteFault tasks = RemoteFault.Unreachable(RemoteFault.Host(ApiPair.TasksAuthority));
        tasks.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(tasks);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateTaskAsync("Debe sobrevivir a la caída");
        await alice.CreateProjectAsync("También");
        OwnedRows before = await api.Monolith.Services.OwnedRowsAsync(alice.AuthId);

        tasks.Active = true;
        SessionResponse during = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        OwnedRows afterFailure = await api.Monolith.Services.OwnedRowsAsync(alice.AuthId);
        tasks.Active = false;
        SessionResponse retry = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        retry.Payload?.Dispose();

        Assert.Equal(new OwnedRows(1, 1, 1, 1, 1, 1, 0), before);
        ProblemAssert.Problem(during, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.Equal(before, afterFailure);
        Assert.Equal(HttpStatusCode.NoContent, retry.Status);
        Assert.Equal(OwnedRows.None, await api.Monolith.Services.OwnedRowsAsync(alice.AuthId));
    }

    [Fact]
    public async Task TasksSilent_TheCallerGivesUpAtItsTimeout_With503()
    {
        RemoteFault tasks = RemoteFault.Silent(RemoteFault.Host(ApiPair.TasksAuthority));
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string> { ["Services:Tasks:Timeout"] = "00:00:00.500" },
            tasks);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await alice.CreateProjectAsync("Lento");

        Stopwatch clock = Stopwatch.StartNew();
        SessionResponse list = await alice.SendAsync(HttpMethod.Get, "api/v1/projects", csrf: false);
        clock.Stop();

        ProblemAssert.Problem(list, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TaskPermissionCopies_OnBothSidesOfTheBoundary_Match()
    {
        Assert.Equal(Constants(typeof(TaskPermissions)), Constants(typeof(TaskPermissionNames)));
    }

    private static Dictionary<string, string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
}
