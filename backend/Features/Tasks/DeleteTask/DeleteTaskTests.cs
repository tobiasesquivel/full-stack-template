using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.DeleteTask;

public sealed class DeleteTaskTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public DeleteTaskTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delete_RemovesOwn_Then_Returns404()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        string id = await alice.CreateTaskAsync("Temporal");

        var deleted = await alice.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{id}");
        deleted.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        var gone = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        gone.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, gone.Status);

        var missing = await alice.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{Guid.NewGuid():D}");
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Delete_Foreign_Returns403_While_AdminDeleteSucceeds()
    {
        var alice = new TestSession(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        var bob = new TestSession(_fixture.CreateClient());
        var bobName = TestAccounts.UniqueName("bob");
        await bob.RegisterAsync(bobName);

        string id = await alice.CreateTaskAsync("Protegida");

        var forbidden = await bob.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{id}");
        forbidden.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);

        await _fixture.SetRoleAsync(bobName, "Admin", member: true);

        var adminDelete = await bob.SendAsync(HttpMethod.Delete, $"api/v1/tasks/{id}");
        adminDelete.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, adminDelete.Status);

        await _fixture.SetRoleAsync(bobName, "Admin", member: false);
    }
}
