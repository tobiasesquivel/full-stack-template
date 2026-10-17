using System.Net;
using TemplateApp.Tests.Support;
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

        var fetched = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.Equal("Propia", fetched.Payload.RootElement.GetProperty("title").GetString());

        var missing = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{Guid.NewGuid():D}", authenticated: true, csrf: false);
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
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

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            object? body = method == HttpMethod.Put ? new { title = "Ajena", isCompleted = true } : null;
            var forbidden = await bob.SendAsync(method, $"api/v1/tasks/{id}", body);
            forbidden.Payload?.Dispose();
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);
        }

        await _fixture.SetRoleAsync(bobName, "Admin", member: true);

        var asAdmin = await bob.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, asAdmin.Status);
        asAdmin.Payload?.Dispose();

        var adminUpdate = await bob.SendAsync(HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Editada por admin", isCompleted = false });
        Assert.Equal(HttpStatusCode.OK, adminUpdate.Status);
        adminUpdate.Payload?.Dispose();

        await _fixture.SetRoleAsync(bobName, "Admin", member: false);

        var afterRevoke = await bob.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        afterRevoke.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.Status);
    }
}
