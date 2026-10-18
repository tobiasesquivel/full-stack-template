using System.Net;
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
}
