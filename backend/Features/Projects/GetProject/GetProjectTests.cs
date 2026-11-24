using System.Net;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Projects.GetProject;

public sealed class GetProjectTests : IClassFixture<ApiFixture>, IDisposable
{
    private readonly ApiFixture _fixture;
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public GetProjectTests(ApiFixture fixture, PostgresFixture postgres, RabbitMqFixture broker)
    {
        _broker = broker.Uri;
        _fixture = fixture;
        _server = postgres.ConnectionString;
    }

    [Fact]
    public async Task GetById_ReturnsOwn_And_Missing_Returns404()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateProjectAsync("Mudanza");

        Assert.Equal("Mudanza", (await alice.ProjectAsync(id)).GetProperty("name").GetString());

        var missing = await alice.SendAsync(HttpMethod.Get, $"api/v1/projects/{Guid.NewGuid():D}", csrf: false);
        ProblemAssert.Problem(missing, HttpStatusCode.NotFound, "Project.NotFound");
    }

    [Fact]
    public async Task Detail_CountsItsTasks_AndNamesItsOwner()
    {
        IsolatedApi api = await StartAsync();
        var alice = new TestSession(api.Pair.CreateClient());
        string aliceName = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(aliceName);
        string id = await alice.CreateProjectAsync("Mudanza");
        string other = await alice.CreateProjectAsync("Viaje");
        string done = await alice.CreateTaskAsync("Embalar", id);
        await alice.CreateTaskAsync("Cargar", id);
        await alice.CreateTaskAsync("Pasaje", other);
        await alice.CreateTaskAsync("Suelta");
        uint version = await alice.TaskVersionAsync(done);
        var completed = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{done}", new { title = "Embalar", isCompleted = true, version });
        completed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, completed.Status);
        await api.DrainOutboxAsync();

        JsonElement project = await alice.ProjectAsync(id);

        Assert.Equal(aliceName, project.GetProperty("ownerName").GetString());
        Assert.Equal(2, project.GetProperty("taskCount").GetInt32());
        Assert.Equal(1, project.GetProperty("completedTaskCount").GetInt32());
        Assert.Equal(1, (await alice.ProjectAsync(other)).GetProperty("taskCount").GetInt32());
    }

    [Fact]
    public async Task Counters_ShowTheOldValue_UntilTheRelayDeliversTheEvent()
    {
        IsolatedApi api = await StartAsync();
        TestSession alice = await api.SignUpAsync("alice");
        string id = await alice.CreateProjectAsync("Mudanza");
        string task = await alice.CreateTaskAsync("Embalar", id);

        Assert.Equal((0, 0), await CountersAsync(alice, id));
        await api.Relay.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal((1, 0), await CountersAsync(alice, id));

        await TaskCounterScenario.SetCompletedAsync(alice, task, true);

        Assert.Equal((1, 0), await CountersAsync(alice, id));
        await api.Relay.RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal((1, 1), await CountersAsync(alice, id));
    }

    [Fact]
    public async Task Read_LoadsNoProject_And_AsksNoOtherProcess()
    {
        CapturedCommands commands = new();
        HttpClient client = _fixture.CreateIsolatedClient(
            services =>
            {
                services.ConfigureDbContext<ProjectsDbContext>(options => options.AddInterceptors(commands));
                services.ConfigureDbContext<TasksDbContext>(options => options.AddInterceptors(commands));
                services.ConfigureDbContext<UsersDbContext>(options => options.AddInterceptors(commands));
            },
            out IServiceProvider services);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateProjectAsync("Sin rastreo");
        await alice.CreateTaskAsync("Embalar", id);

        using IServiceScope scope = services.CreateScope();
        commands.Reset();
        ErrorOr<ProjectResponse> read = await scope.Handler<GetProject.Handler>()
            .Handle(new GetProjectCommand(alice.AuthId, Guid.Parse(id)), TestContext.Current.CancellationToken);

        Assert.False(read.IsError);
        Assert.DoesNotContain("\"NormalizedName\"", Assert.Single(commands.Reading("Projects")).Text, StringComparison.Ordinal);
        Assert.Single(commands.Reading("ProjectTaskCounters"));
        Assert.Empty(commands.Reading("TaskItems"));
        Assert.Single(commands.Reading("UserProfiles"));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ChangeTracker.Entries<Project>());
    }

    [Fact]
    public async Task ForeignProject_IsForbidden_Then_AdminReadsAndUpdatesIt()
    {
        var alice = new TestSession(_fixture.CreateClient());
        string aliceName = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(aliceName);
        var bob = new TestSession(_fixture.CreateClient());
        string bobName = TestAccounts.UniqueName("bob");
        await bob.RegisterAsync(bobName);
        string id = await alice.CreateProjectAsync("Mudanza");
        uint version = (await alice.ProjectAsync(id)).GetProperty("version").GetUInt32();

        var read = await bob.SendAsync(HttpMethod.Get, $"api/v1/projects/{id}", csrf: false);
        ProblemAssert.Problem(read, HttpStatusCode.Forbidden, "Project.Forbidden");
        var update = await bob.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza", isArchived = true, version });
        ProblemAssert.Problem(update, HttpStatusCode.Forbidden, "Project.Forbidden");

        await _fixture.SetRoleAsync(bobName, "Admin", member: true);

        var asAdmin = await bob.SendAsync(HttpMethod.Get, $"api/v1/projects/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, asAdmin.Status);
        Assert.NotNull(asAdmin.Payload);
        using (asAdmin.Payload)
            Assert.Equal(aliceName, asAdmin.Payload.RootElement.GetProperty("ownerName").GetString());

        var adminUpdate = await bob.SendAsync(HttpMethod.Put, $"api/v1/projects/{id}", new { name = "Mudanza", isArchived = true, version });
        adminUpdate.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, adminUpdate.Status);
        Assert.True((await alice.ProjectAsync(id)).GetProperty("isArchived").GetBoolean());
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static async Task<(int Tasks, int Completed)> CountersAsync(TestSession session, string projectId)
    {
        JsonElement project = await session.ProjectAsync(projectId);
        return (project.GetProperty("taskCount").GetInt32(), project.GetProperty("completedTaskCount").GetInt32());
    }

    private async Task<IsolatedApi> StartAsync()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker);
        _apis.Add(api);
        return api;
    }
}
