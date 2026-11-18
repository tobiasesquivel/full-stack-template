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

namespace TemplateApp.Features.Projects.ListProjects;

public sealed class ListProjectsTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ListProjectsTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_PagesOwnProjects_NewestFirst_WithACursor()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));
        foreach (string name in new[] { "Uno", "Dos", "Tres" })
            await alice.CreateProjectAsync(name);
        await bob.CreateProjectAsync("Ajeno");

        (string[] first, string? cursor) = await PageAsync(alice, "api/v1/projects?pageSize=2");
        Assert.Equal(["Tres", "Dos"], first);
        Assert.NotNull(cursor);

        (string[] second, string? end) = await PageAsync(alice, $"api/v1/projects?pageSize=2&cursor={cursor}");
        Assert.Equal(["Uno"], second);
        Assert.Null(end);
    }

    [Fact]
    public async Task List_RejectsABadCursor_AndAPageSizeOutOfRange()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var badCursor = await alice.SendAsync(HttpMethod.Get, "api/v1/projects?cursor=abc", csrf: false);
        Assert.True(ProblemAssert.Problem(badCursor, HttpStatusCode.BadRequest, "Validation.Failed").GetProperty("errors").TryGetProperty("Cursor", out _));

        foreach (int pageSize in new[] { 0, 101 })
        {
            var badSize = await alice.SendAsync(HttpMethod.Get, $"api/v1/projects?pageSize={pageSize}", csrf: false);
            Assert.True(ProblemAssert.Problem(badSize, HttpStatusCode.BadRequest, "Validation.Failed").GetProperty("errors").TryGetProperty("PageSize", out _));
        }
    }

    [Fact]
    public async Task List_ComposesAPageOfFive_WithOneQueryPerFeature_AndTracksNoProject()
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
        string aliceName = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(aliceName);
        Dictionary<Guid, int> tasksPerProject = [];
        for (int i = 1; i <= 5; i++)
        {
            string projectId = await alice.CreateProjectAsync($"Proyecto {i}");
            for (int t = 0; t < i; t++)
                await alice.CreateTaskAsync("Tarea", projectId);
            tasksPerProject[Guid.Parse(projectId)] = i;
        }

        using IServiceScope scope = services.CreateScope();
        commands.Reset();
        ErrorOr<ProjectListResponse> page = await scope.ServiceProvider.GetRequiredService<ListProjects>()
            .ExecuteAsync(alice.AuthId, cursor: null, pageSize: 5, TestContext.Current.CancellationToken);

        Assert.False(page.IsError);
        Assert.Equal(5, page.Value.Items.Count);
        Assert.All(page.Value.Items, item =>
        {
            Assert.Equal(tasksPerProject[item.Id], item.TaskCount);
            Assert.Equal(aliceName, item.OwnerName);
        });
        Assert.Single(commands.Reading("Projects"));
        Assert.Single(commands.Reading("TaskItems"));
        Assert.Single(commands.Reading("UserProfiles"));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ChangeTracker.Entries<Project>());
    }

    private static async Task<(string[] Names, string? NextCursor)> PageAsync(TestSession session, string route)
    {
        var page = await session.SendAsync(HttpMethod.Get, route, csrf: false);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotNull(page.Payload);
        using (page.Payload)
        {
            JsonElement root = page.Payload.RootElement;
            JsonElement next = root.GetProperty("nextCursor");
            return (
                [.. root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)],
                next.ValueKind == JsonValueKind.Null ? null : next.GetString());
        }
    }
}
