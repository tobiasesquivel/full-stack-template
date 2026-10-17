using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.CreateTask;

public sealed class CreateTaskTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CreateTaskTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_Returns201_WithTrimmedTitle()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var response = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "  Primera tarea  " });

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            var created = response.Payload.RootElement;
            Assert.Equal("Primera tarea", created.GetProperty("title").GetString());
            Assert.False(created.GetProperty("isCompleted").GetBoolean());
            Assert.NotEqual(Guid.Empty.ToString(), created.GetProperty("id").GetString());
            Assert.False(created.TryGetProperty("ownerId", out _), "OwnerId must not appear in task JSON.");
        }
    }

    [Fact]
    public async Task Create_RejectsBlankAndOversizedTitles()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        var blank = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "   " });
        blank.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);

        var oversized = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = new string('x', 201) });
        oversized.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, oversized.Status);
    }
}
