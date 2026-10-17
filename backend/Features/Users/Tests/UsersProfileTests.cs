using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.Tests;

public sealed class UsersProfileTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UsersProfileTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private TestSession NewSession() => new(_fixture.CreateClient());

    private static string UniqueName(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 16, prefix.Length + 32)];

    private async Task CsrfAsync(TestSession session)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", authenticated: false, csrf: false);
        response.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(session.Cookies.ContainsKey("XSRF-TOKEN"));
    }

    private async Task RegisterAsync(TestSession session, string userName)
    {
        await CsrfAsync(session);
        session.UserName = userName;
        session.Password = $"Smoke!{Guid.NewGuid():N}Aa9";

        SessionResponse response = await session.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName, email = $"{userName}@example.test", password = session.Password, displayName = userName },
            authenticated: false);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            JsonElement root = response.Payload.RootElement;
            session.AccessToken = root.GetProperty("accessToken").GetString();
            session.AuthId = root.GetProperty("user").GetProperty("authId").GetString()!;
        }
    }

    private async Task PromoteAsync(string userName, string role)
    {
        using IServiceScope scope = _fixture.Services.CreateScope();
        UserManager<AppIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        AppIdentityUser? identity = await users.FindByNameAsync(userName);
        Assert.NotNull(identity);
        IdentityResult result = await users.AddToRoleAsync(identity, role);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetMe_ReturnsProfile_WithTasksCreateOnly()
    {
        TestSession alice = NewSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);

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
    public async Task UpdateMe_Persists_DisplayName_And_Timezone_Then_RejectsInvalidZone()
    {
        TestSession alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        SessionResponse updated = await alice.SendAsync(
            HttpMethod.Put,
            "api/v1/users/me",
            new { displayName = "Perfil de smoke", timezone = "America/Argentina/Buenos_Aires" });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.NotNull(updated.Payload);
        using (updated.Payload)
        {
            Assert.Equal("Perfil de smoke", updated.Payload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("America/Argentina/Buenos_Aires", updated.Payload.RootElement.GetProperty("timezone").GetString());
        }

        SessionResponse reread = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, reread.Status);
        Assert.NotNull(reread.Payload);
        using (reread.Payload)
        {
            Assert.Equal("Perfil de smoke", reread.Payload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal(alice.UserName, reread.Payload.RootElement.GetProperty("userName").GetString());
        }

        SessionResponse invalid = await alice.SendAsync(
            HttpMethod.Put,
            "api/v1/users/me",
            new { displayName = "Perfil", timezone = "No/Existe" });

        invalid.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
    }

    [Fact]
    public async Task ForeignProfile_IsForbidden_WithoutPermission_Then_AllowedWithPermission()
    {
        TestSession alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));
        TestSession bob = NewSession();
        string bobName = UniqueName("bob");
        await RegisterAsync(bob, bobName);

        SessionResponse forbidden = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{alice.AuthId}", authenticated: true, csrf: false);
        forbidden.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);

        SessionResponse missing = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{Guid.NewGuid():D}", authenticated: true, csrf: false);
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);

        await PromoteAsync(bobName, "Admin");

        SessionResponse allowed = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/{alice.AuthId}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, allowed.Status);
        allowed.Payload?.Dispose();
    }

    [Fact]
    public async Task DeleteMe_Cascades_And_AccessBecomes401()
    {
        TestSession alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        SessionResponse created = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Para cascada" });
        created.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, created.Status);

        SessionResponse deleted = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        deleted.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        SessionResponse denied = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);

        await CsrfAsync(alice);
        SessionResponse relogin = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);

        relogin.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.Status);
    }
}
