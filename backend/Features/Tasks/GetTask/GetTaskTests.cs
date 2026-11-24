using System.Net;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
using Xunit;

namespace TemplateApp.Features.Tasks.GetTask;

public sealed class GetTaskTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetTaskTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetById_ReturnsOwn_And_Missing_Returns404()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        string id = await alice.CreateTaskAsync("Propia");

        var fetched = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.Equal("Propia", fetched.Payload.RootElement.GetProperty("title").GetString());

        var missing = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{Guid.NewGuid():D}", csrf: false);
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Detail_EqualsItsRowInTheList_ForOpenCompletedAndReopened()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        string open = await alice.CreateTaskAsync("Abierta");
        string completed = await alice.CreateTaskAsync("Completada");
        await PutAsync(alice, completed, "Completada", isCompleted: true);
        string reopened = await alice.CreateTaskAsync("Reabierta");
        await PutAsync(alice, reopened, "Reabierta", isCompleted: true);
        await PutAsync(alice, reopened, "Reabierta otra vez", isCompleted: false);

        var list = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks?pageSize=10", csrf: false);
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.NotNull(list.Payload);
        Dictionary<string, string> rows;
        using (list.Payload)
            rows = list.Payload.RootElement.GetProperty("items").EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetRawText());

        foreach (string id in new[] { open, completed, reopened })
        {
            var detail = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
            Assert.Equal(HttpStatusCode.OK, detail.Status);
            Assert.NotNull(detail.Payload);
            using (detail.Payload)
                Assert.Equal(rows[id], detail.Payload.RootElement.GetRawText());
        }
    }

    [Fact]
    public async Task Read_IsOneQuery_And_LeavesNoTaskTracked()
    {
        CapturedCommands commands = new();
        HttpClient client = _fixture.CreateIsolatedTasksClient(
            services => services.ConfigureDbContext<TasksDbContext>(options => options.AddInterceptors(commands)),
            out IServiceProvider services);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string id = await alice.CreateTaskAsync("Sin rastreo");

        using IServiceScope scope = services.CreateScope();
        commands.Reset();
        ErrorOr<TaskResponse> read = await ActivatorUtilities.CreateInstance<GetTask.Handler>(scope.ServiceProvider)
            .Handle(new GetTaskCommand(new Caller(alice.AuthId, new HashSet<string>()), Guid.Parse(id)), TestContext.Current.CancellationToken);

        Assert.False(read.IsError);
        Assert.Equal("Sin rastreo", read.Value.Title);
        Assert.Single(commands.Reading("TaskItems"));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<TasksDbContext>().ChangeTracker.Entries<TaskItem>());
    }

    private static async Task PutAsync(TestSession session, string id, string title, bool isCompleted)
    {
        uint version = await session.TaskVersionAsync(id);
        var updated = await session.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title, isCompleted, version });
        updated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, updated.Status);
    }

    [Fact]
    public async Task ForeignTask_IsForbidden_Then_AdminSeesIt_Then_RevokeHidesItAgain()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        var bobName = TestAccounts.UniqueName("bob");
        await bob.RegisterAsync(bobName);

        string id = await alice.CreateTaskAsync("Ajena");
        uint read = await alice.TaskVersionAsync(id);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            object? body = method == HttpMethod.Put ? new { title = "Ajena", isCompleted = true, version = read } : null;
            var forbidden = await bob.SendAsync(method, $"api/v1/tasks/{id}", body);
            forbidden.Payload?.Dispose();
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);
        }

        await _fixture.SetRoleAsync(bobName, "Admin", member: true);

        var asAdmin = await bob.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, asAdmin.Status);
        asAdmin.Payload?.Dispose();

        var adminUpdate = await bob.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Editada por admin", isCompleted = false, version = read });
        Assert.Equal(HttpStatusCode.OK, adminUpdate.Status);
        adminUpdate.Payload?.Dispose();

        await _fixture.SetRoleAsync(bobName, "Admin", member: false);

        var afterRevoke = await bob.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        afterRevoke.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.Status);
    }
}
