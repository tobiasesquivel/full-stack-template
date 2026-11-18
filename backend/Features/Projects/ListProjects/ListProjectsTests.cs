using System.Net;
using System.Text.Json;
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
