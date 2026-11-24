using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
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
    public async Task List_FollowsNextCursor_UntilTheLastPage()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var created = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in new[] { "Tarea uno", "Tarea dos", "Tarea tres" })
            created.Add(await alice.CreateTaskAsync(title));

        var first = await ReadPageAsync(alice, "api/v1/tasks?pageSize=2");
        Assert.Equal(2, first.Ids.Count);
        Assert.NotNull(first.NextCursor);

        var second = await ReadPageAsync(alice, $"api/v1/tasks?pageSize=2&cursor={first.NextCursor}");
        Assert.Single(second.Ids);
        Assert.Null(second.NextCursor);

        Assert.Equal(created, first.Ids.Concat(second.Ids).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task List_DoesNotRepeatOrSkip_WhenTasksAreCreatedBetweenPages()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var original = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i <= 5; i++)
            original.Add(await alice.CreateTaskAsync($"Original {i}"));

        var first = await ReadPageAsync(alice, "api/v1/tasks?pageSize=2");
        var later = new[] { await alice.CreateTaskAsync("Nueva 1"), await alice.CreateTaskAsync("Nueva 2") };

        var seen = await ReadRestAsync(alice, first);

        Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(original, seen.ToHashSet(StringComparer.Ordinal));
        Assert.DoesNotContain(later[0], seen);
        Assert.DoesNotContain(later[1], seen);
    }

    [Fact]
    public async Task List_DoesNotRepeatOrSkip_WhenTasksShareTheSameInstant()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = _fixture.CreateIsolatedClient(services => services.AddSingleton<TimeProvider>(clock), out _);
        var alice = new TestSession(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        for (var i = 1; i <= 5; i++)
            await alice.CreateTaskAsync($"Empate {i}");

        var whole = await ReadPageAsync(alice, "api/v1/tasks?pageSize=100");
        var first = await ReadPageAsync(alice, "api/v1/tasks?pageSize=2");
        var seen = await ReadRestAsync(alice, first);

        Assert.Equal(whole.Ids, seen);
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("AAAA")]
    [InlineData("UNKNOWN_VERSION")]
    public async Task List_RejectsAnUnreadableCursor(string cursor)
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        if (cursor == "UNKNOWN_VERSION")
        {
            var bytes = new byte[25];
            bytes[0] = 9;
            cursor = Base64Url.EncodeToString(bytes);
        }

        var response = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks?cursor={cursor}", csrf: false);

        var problem = ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Validation.Failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("Cursor", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_RejectsAPageSizeOutOfRange(int pageSize)
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var response = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks?pageSize={pageSize}", csrf: false);

        var problem = ProblemAssert.Problem(response, HttpStatusCode.BadRequest, "Validation.Failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("PageSize", out _));
    }

    [Fact]
    public async Task List_IsolatesOwners()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        await bob.RegisterAsync(TestAccounts.UniqueName("bob"));

        await alice.CreateTaskAsync("Solo Alice");

        var bobPage = await ReadPageAsync(bob, "api/v1/tasks");
        Assert.Empty(bobPage.Ids);
        Assert.Null(bobPage.NextCursor);
    }

    private static async Task<List<string>> ReadRestAsync(TestSession session, TaskPage first)
    {
        var seen = new List<string>(first.Ids);
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var next = await ReadPageAsync(session, $"api/v1/tasks?pageSize=2&cursor={cursor}");
            seen.AddRange(next.Ids);
            cursor = next.NextCursor;
        }

        return seen;
    }

    private static async Task<TaskPage> ReadPageAsync(TestSession session, string route)
    {
        var response = await session.SendAsync(HttpMethod.Get, route, csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            var page = response.Payload.RootElement;
            var ids = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();
            var next = page.GetProperty("nextCursor");
            return new TaskPage(ids, next.ValueKind == JsonValueKind.Null ? null : next.GetString());
        }
    }

    private sealed record TaskPage(List<string> Ids, string? NextCursor);
}
