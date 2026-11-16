using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.UpdateTask;

public sealed class UpdateTaskTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UpdateTaskTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Update_PersistsFinalState_Idempotently()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        string id = await alice.CreateTaskAsync("Borrador");

        var updated = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.NotNull(updated.Payload);
        using (updated.Payload)
        {
            Assert.Equal("Estado final", updated.Payload.RootElement.GetProperty("title").GetString());
            Assert.True(updated.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }

        var repeated = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true });
        Assert.Equal(HttpStatusCode.OK, repeated.Status);
        repeated.Payload?.Dispose();

        var fetched = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.True(fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());

        var invalid = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "", isCompleted = false });
        invalid.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        var missing = await alice.SendAsync(HttpMethod.Put, $"api/v1/tasks/{Guid.NewGuid():D}", new { title = "x", isCompleted = false });
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409_And_KeepsOtherTabChange()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());

        string id = await tabA.CreateTaskAsync("Comprar pan");
        uint? read = await ReadVersionAsync(tabA, id);

        var renamed = await tabA.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan integral", isCompleted = false, version = read });
        renamed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, renamed.Status);

        var stale = await tabB.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Comprar pan", isCompleted = true, version = read });

        var fetched = await tabA.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
        {
            Assert.Equal("Comprar pan integral", fetched.Payload.RootElement.GetProperty("title").GetString());
            Assert.False(fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }

        ProblemAssert.Problem(stale, HttpStatusCode.Conflict, "Task.Conflict");
    }

    [Fact]
    public async Task Update_ConcurrentWithSameVersion_OneWins_OtherGets409()
    {
        var tabA = new TestSession(_fixture.CreateClient());
        await tabA.RegisterAsync(TestAccounts.UniqueName("alice"));
        var tabB = tabA.CopyTo(_fixture.CreateClient());

        string id = await tabA.CreateTaskAsync("Comprar pan");
        uint? read = await ReadVersionAsync(tabA, id);

        var a = new { title = "Comprar pan integral", isCompleted = false, version = read };
        var b = new { title = "Comprar pan", isCompleted = true, version = read };
        SessionResponse[] responses = await Task.WhenAll(
            tabA.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", a),
            tabB.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", b));
        foreach (SessionResponse response in responses)
            response.Payload?.Dispose();

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.Status).Order().ToArray());

        var winner = responses[0].Status == HttpStatusCode.OK ? a : b;
        var fetched = await tabA.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
        {
            Assert.Equal(winner.title, fetched.Payload.RootElement.GetProperty("title").GetString());
            Assert.Equal(winner.isCompleted, fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }
    }

    private static async Task<uint?> ReadVersionAsync(TestSession session, string id)
    {
        var fetched = await session.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            return fetched.Payload.RootElement.TryGetProperty("version", out JsonElement version) ? version.GetUInt32() : null;
    }
}
