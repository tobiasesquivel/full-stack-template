using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.ListTasks;

public sealed class ListTasksTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ListTasksTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_ReturnsOwnTasks_WithPagingShape()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        foreach (var title in new[] { "Tarea uno", "Tarea dos", "Tarea tres" })
            await alice.CreateTaskAsync(title);

        var firstPage = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks?page=1&pageSize=2", csrf: false);
        Assert.Equal(HttpStatusCode.OK, firstPage.Status);
        Assert.NotNull(firstPage.Payload);
        List<string> firstIds;
        using (firstPage.Payload)
        {
            var page = firstPage.Payload.RootElement;
            Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, page.GetProperty("page").GetInt32());
            Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
            Assert.Equal(2, page.GetProperty("items").GetArrayLength());
            firstIds = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
        }

        var secondPage = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks?page=2&pageSize=2", csrf: false);
        Assert.Equal(HttpStatusCode.OK, secondPage.Status);
        Assert.NotNull(secondPage.Payload);
        using (secondPage.Payload)
        {
            var page = secondPage.Payload.RootElement;
            Assert.Equal(1, page.GetProperty("items").GetArrayLength());
            var secondIds = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
            Assert.Equal(3, new HashSet<string>(firstIds.Concat(secondIds)).Count);
        }

        var tooWide = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks?pageSize=101", csrf: false);
        tooWide.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.Status);

        var zeroPage = await alice.SendAsync(HttpMethod.Get, "api/v1/tasks?page=0", csrf: false);
        zeroPage.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, zeroPage.Status);
    }

    [Fact]
    public async Task List_IsolatesOwners()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));

        await alice.CreateTaskAsync("Solo Alice");

        var bobPage = await bob.SendAsync(HttpMethod.Get, "api/v1/tasks", csrf: false);
        Assert.Equal(HttpStatusCode.OK, bobPage.Status);
        Assert.NotNull(bobPage.Payload);
        using (bobPage.Payload)
        {
            var page = bobPage.Payload.RootElement;
            Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        }
    }
}
