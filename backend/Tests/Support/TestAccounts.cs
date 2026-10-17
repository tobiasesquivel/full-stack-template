using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using Xunit;

namespace TemplateApp.Tests.Support;

internal static class TestAccounts
{
    public static string UniqueName(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 16, prefix.Length + 32)];

    public static async Task CsrfAsync(this TestSession session)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", authenticated: false, csrf: false);
        response.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(session.Cookies.ContainsKey("XSRF-TOKEN"), "CSRF endpoint must issue XSRF-TOKEN.");
    }

    public static async Task RegisterAsync(this TestSession session, string userName)
    {
        await session.CsrfAsync();
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
            Assert.False(root.TryGetProperty("refreshToken", out _), "Refresh token must not appear in JSON.");
            session.AccessToken = root.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrEmpty(session.AccessToken));
            Assert.True(DateTimeOffset.TryParse(root.GetProperty("accessTokenExpiresAt").GetString(), out _));
            JsonElement user = root.GetProperty("user");
            session.AuthId = user.GetProperty("authId").GetString()!;
            Assert.Equal(userName, user.GetProperty("userName").GetString());
            Assert.Equal("UTC", user.GetProperty("timezone").GetString());
            string[] permissions = user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();
            Assert.Equal(["Permissions.Tasks.Create"], permissions);
            Assert.DoesNotContain("refreshToken", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(session.Cookies.Values, c => c.HttpOnly && c.Path == "/api/v1/auth");
    }

    public static async Task SetRoleAsync(this ApiFixture fixture, string userName, string role, bool member)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        UserManager<AppIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        AppIdentityUser? identity = await users.FindByNameAsync(userName);
        Assert.NotNull(identity);
        IdentityResult result = member
            ? await users.AddToRoleAsync(identity, role)
            : await users.RemoveFromRoleAsync(identity, role);
        Assert.True(result.Succeeded);
    }

    public static async Task<string> CreateTaskAsync(this TestSession session, string title)
    {
        SessionResponse created = await session.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        using (created.Payload)
            return created.Payload.RootElement.GetProperty("id").GetString()!;
    }

    public static void AssertJwtIsIdentityOnly(string accessToken)
    {
        string payload = accessToken.Split('.')[1];
        JsonDocument claims = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=')));
        using (claims)
        {
            JsonElement root = claims.RootElement;
            Assert.True(root.TryGetProperty("sub", out _));
            Assert.True(root.TryGetProperty("sid", out _));
            Assert.False(root.TryGetProperty("role", out _));
            Assert.False(root.TryGetProperty("roles", out _));
            Assert.False(root.TryGetProperty("permission", out _));
            Assert.False(root.TryGetProperty("permissions", out _));
        }
    }
}
