using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Identity;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.Tests;

public sealed class UsersProfileTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public UsersProfileTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class Session
    {
        public HttpClient Client = null!;
        public Dictionary<string, CookieDatum> Cookies = new(StringComparer.Ordinal);
        public string? AccessToken;
        public string UserName = string.Empty;
        public string AuthId = string.Empty;
        public string Password = string.Empty;
    }

    private sealed class CookieDatum
    {
        public string Value = string.Empty;
        public string Path = "/";
        public bool HttpOnly;
    }

    private sealed class Response
    {
        public HttpStatusCode Status;
        public JsonDocument? Payload;
    }

    private Session NewSession() => new() { Client = _fixture.CreateClient() };

    private static string UniqueName(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 16, prefix.Length + 32)];

    private async Task<Response> SendAsync(
        Session session,
        HttpMethod method,
        string route,
        object? body = null,
        bool authenticated = true,
        bool csrf = true)
    {
        using HttpRequestMessage request = new(method, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (session.Cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value.Value}")));

        if (authenticated && session.AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        if (csrf && method != HttpMethod.Get && session.Cookies.TryGetValue("XSRF-TOKEN", out CookieDatum? xsrf))
            request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(xsrf.Value));

        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using HttpResponseMessage httpResponse = await session.Client.SendAsync(request);

        if (httpResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
        {
            foreach (string raw in setCookies)
            {
                string[] parts = raw.Split(';').Select(p => p.Trim()).ToArray();
                string pair = parts[0];
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;
                string name = pair[..equals];
                string value = pair[(equals + 1)..];
                bool expired = parts.Any(p => p.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase))
                    || parts.Where(p => p.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase))
                        .Any(p => DateTimeOffset.TryParse(p["Expires=".Length..], out DateTimeOffset exp) && exp <= DateTimeOffset.UtcNow);
                if (expired || value.Length == 0)
                {
                    session.Cookies.Remove(name);
                    continue;
                }
                string path = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?["Path=".Length..] ?? "/";
                bool httpOnly = parts.Any(p => p.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
                session.Cookies[name] = new CookieDatum { Value = value, Path = path, HttpOnly = httpOnly };
            }
        }

        string text = await httpResponse.Content.ReadAsStringAsync();
        return new Response
        {
            Status = httpResponse.StatusCode,
            Payload = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text),
        };
    }

    private async Task CsrfAsync(Session session)
    {
        Response response = await SendAsync(session, HttpMethod.Get, "api/v1/auth/csrf", authenticated: false, csrf: false);
        response.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(session.Cookies.ContainsKey("XSRF-TOKEN"));
    }

    private async Task RegisterAsync(Session session, string userName)
    {
        await CsrfAsync(session);
        session.UserName = userName;
        session.Password = $"Smoke!{Guid.NewGuid():N}Aa9";

        Response response = await SendAsync(
            session,
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
        Session alice = NewSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);

        Response response = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
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
        Session alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        Response updated = await SendAsync(
            alice,
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

        Response reread = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, reread.Status);
        Assert.NotNull(reread.Payload);
        using (reread.Payload)
        {
            Assert.Equal("Perfil de smoke", reread.Payload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal(alice.UserName, reread.Payload.RootElement.GetProperty("userName").GetString());
        }

        Response invalid = await SendAsync(
            alice,
            HttpMethod.Put,
            "api/v1/users/me",
            new { displayName = "Perfil", timezone = "No/Existe" });

        invalid.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
    }

    [Fact]
    public async Task ForeignProfile_IsForbidden_WithoutPermission_Then_AllowedWithPermission()
    {
        Session alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));
        Session bob = NewSession();
        string bobName = UniqueName("bob");
        await RegisterAsync(bob, bobName);

        Response forbidden = await SendAsync(bob, HttpMethod.Get, $"api/v1/users/{alice.AuthId}", authenticated: true, csrf: false);
        forbidden.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);

        Response missing = await SendAsync(bob, HttpMethod.Get, $"api/v1/users/{Guid.NewGuid():D}", authenticated: true, csrf: false);
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);

        await PromoteAsync(bobName, "Admin");

        Response allowed = await SendAsync(bob, HttpMethod.Get, $"api/v1/users/{alice.AuthId}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, allowed.Status);
        allowed.Payload?.Dispose();
    }

    [Fact]
    public async Task DeleteMe_Cascades_And_AccessBecomes401()
    {
        Session alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        Response created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Para cascada" });
        created.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, created.Status);

        Response deleted = await SendAsync(alice, HttpMethod.Delete, "api/v1/users/me");
        deleted.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        Response denied = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);

        await CsrfAsync(alice);
        Response relogin = await SendAsync(
            alice,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);

        relogin.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.Status);
    }
}
