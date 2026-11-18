using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Infrastructure.Persistence;
using Xunit;

namespace TemplateApp.Tests.Support;

internal static class TestAccounts
{
    public const string SessionCookieName = "__Host-session";
    public const string XsrfCookieName = "XSRF-TOKEN";

    public static string UniqueName(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 16, prefix.Length + 32)];

    public static async Task CsrfAsync(this TestSession session)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", csrf: false);
        response.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(session.Cookies.ContainsKey(XsrfCookieName), "CSRF endpoint must issue XSRF-TOKEN.");
    }

    public static async Task<SessionResponse> RegisterAsync(this TestSession session, string userName)
    {
        await session.CsrfAsync();
        session.UserName = userName;
        session.Password = $"Smoke!{Guid.NewGuid():N}Aa9";

        SessionResponse response = await session.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName, email = $"{userName}@example.test", password = session.Password, displayName = userName });

        Assert.Equal(HttpStatusCode.Created, response.Status);
        AssertCarriesNoToken(response);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            JsonElement user = response.Payload.RootElement;
            session.AuthId = user.GetProperty("authId").GetString()!;
            Assert.Equal(userName, user.GetProperty("userName").GetString());
            Assert.Equal("UTC", user.GetProperty("timezone").GetString());
            string[] permissions = user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();
            Assert.Equal(["Permissions.Tasks.Create", "Permissions.Projects.Create"], permissions);
        }

        AssertSessionCookie(session);
        return response;
    }

    public static async Task<SessionResponse> OpenAsync(this TestSession session)
    {
        await session.CsrfAsync();
        return await session.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = session.UserName, password = session.Password });
    }

    public static void AssertSessionCookie(TestSession session)
    {
        Assert.True(session.Cookies.TryGetValue(SessionCookieName, out SessionCookie? cookie), "The session must use the __Host-session cookie.");
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal("/", cookie.Path);
        Assert.Equal("strict", cookie.SameSite, ignoreCase: true);
    }

    public static void AssertCarriesNoToken(SessionResponse response)
    {
        Assert.DoesNotContain("token", response.Text, StringComparison.OrdinalIgnoreCase);
        foreach (string raw in response.SetCookies)
        {
            string name = raw.Split('=')[0];
            bool httpOnly = raw.Split(';').Any(p => p.Trim().Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
            if (!httpOnly)
                Assert.Equal(XsrfCookieName, name);
        }
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

    public static async Task<int> SqlAsync(this ApiFixture fixture, FormattableString sql)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database.ExecuteSqlAsync(sql);
    }

    public static async Task<List<DateTimeOffset>> LastSeenAsync(this ApiFixture fixture, string authId)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.Database
            .SqlQuery<DateTimeOffset>($"SELECT \"LastSeenAt\" AS \"Value\" FROM \"Sessions\" WHERE \"UserId\" = {authId}")
            .ToListAsync();
    }

    public static async Task<int> IdempotencyKeyCountAsync(this ApiFixture fixture, string authId)
    {
        using IServiceScope scope = fixture.Services.CreateScope();
        PlatformDbContext db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        return await db.IdempotencyKeys.CountAsync(k => k.OwnerId == authId);
    }

    public static async Task<string> CreateTaskAsync(this TestSession session, string title)
    {
        SessionResponse created = await session.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        using (created.Payload)
            return created.Payload.RootElement.GetProperty("id").GetString()!;
    }

    public static async Task<string> CreateProjectAsync(this TestSession session, string name)
    {
        SessionResponse created = await session.SendAsync(HttpMethod.Post, "api/v1/projects", new { name });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        using (created.Payload)
            return created.Payload.RootElement.GetProperty("id").GetString()!;
    }

    public static async Task<JsonElement> ProjectAsync(this TestSession session, string id)
    {
        SessionResponse fetched = await session.SendAsync(HttpMethod.Get, $"api/v1/projects/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            return fetched.Payload.RootElement.Clone();
    }

    public static async Task<uint> TaskVersionAsync(this TestSession session, string id)
    {
        SessionResponse fetched = await session.SendAsync(HttpMethod.Get, $"api/v1/tasks/{id}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            return fetched.Payload.RootElement.GetProperty("version").GetUInt32();
    }
}
