using System.Diagnostics;
using System.Net;
using System.Reflection;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
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

        Assert.Equal(3, auth.Calls);
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
        Assert.Equal(3, projects.Calls);
        Assert.Equal(new Dictionary<string, long> { ["auth.AspNetUsers"] = 1, ["auth.AspNetUserRoles"] = 1, ["auth.Sessions"] = 1, ["users.UserProfiles"] = 1, ["tasks.TaskItems"] = 1, ["projects.Projects"] = 1 }, (await api.OwnedRowsAsync(alice.AuthId)).NonZero);
        Assert.False(string.IsNullOrEmpty(loose));
    }

    [Fact]
    public async Task TasksUnreachable_ProjectReadsAnswerTheLastCounters_WithoutCallingTasks()
    {
        RemoteFault tasks = RemoteFault.Unreachable(RemoteFault.Host(ApiPair.TasksAuthority));
        tasks.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(tasks);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string project = await alice.CreateProjectAsync("Antes de la caída");
        tasks.Active = true;

        SessionResponse created = await alice.SendAsync(HttpMethod.Post, "api/v1/projects", new { name = "Nuevo" });
        SessionResponse list = await alice.SendAsync(HttpMethod.Get, "api/v1/projects", csrf: false);
        SessionResponse detail = await alice.SendAsync(HttpMethod.Get, $"api/v1/projects/{project}", csrf: false);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        created.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.NotNull(list.Payload);
        using (list.Payload)
            Assert.Equal(2, list.Payload.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, detail.Status);
        Assert.NotNull(detail.Payload);
        using (detail.Payload)
            Assert.Equal(0, detail.Payload.RootElement.GetProperty("taskCount").GetInt32());
        Assert.Equal(0, tasks.Calls);
    }

    [Fact]
    public async Task ProjectsSilent_EachAttemptTimesOut_AndTheTotalTimeoutAnswers503()
    {
        RemoteFault projects = RemoteFault.Silent(request =>
            RemoteFault.Host(ApiPair.MonolithAuthority)(request) && RemoteFault.Path(OwnerScoped)(request));
        projects.Active = false;
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string>
            {
                ["Services:Monolith:Timeout"] = "00:00:00.300",
                ["Services:Monolith:TotalTimeout"] = "00:00:00.700",
                ["Services:Monolith:Retries"] = "5",
                ["Services:Monolith:RetryDelay"] = "00:00:00.010",
            },
            projects);
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string project = await alice.CreateProjectAsync("Lento");

        projects.Active = true;
        Stopwatch clock = Stopwatch.StartNew();
        SessionResponse created = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Embalar", projectId = project });
        clock.Stop();

        ProblemAssert.Problem(created, HttpStatusCode.ServiceUnavailable, "Http.ServiceUnavailable");
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1500));
        Assert.InRange(projects.Calls, 2, 3);
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
