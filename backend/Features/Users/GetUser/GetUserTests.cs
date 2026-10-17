using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.GetUser;

public sealed class GetUserTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetUserTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetMe_ReturnsProfile_WithTasksCreateOnly()
    {
        TestSession alice = new(_fixture.CreateClient());
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);

        SessionResponse response = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            JsonElement profile = response.Payload.RootElement;
            Assert.Equal(alice.AuthId, profile.GetProperty("authId").GetString());
            Assert.Equal(name, profile.GetProperty("userName").GetString());
            Assert.Equal($"{name}@example.test", profile.GetProperty("email").GetString());
            Assert.Equal(name, profile.GetProperty("displayName").GetString());
            Assert.Equal("UTC", profile.GetProperty("timezone").GetString());
            string[] permissions = profile.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();
            Assert.Equal(["Permissions.Tasks.Create"], permissions);
        }
    }

    [Fact]
    public async Task ForeignProfile_IsForbidden_WithoutPermission_Then_AllowedWithPermission()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession bob = new(_fixture.CreateClient());
        string bobName = TestAccounts.UniqueName("bob");
        await bob.RegisterAsync(bobName);

        SessionResponse forbidden = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{alice.AuthId}", authenticated: true, csrf: false);
        forbidden.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);

        SessionResponse missing = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{Guid.NewGuid():D}", authenticated: true, csrf: false);
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);

        await _fixture.SetRoleAsync(bobName, "Admin", member: true);

        SessionResponse allowed = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{alice.AuthId}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, allowed.Status);
        allowed.Payload?.Dispose();
    }
}
