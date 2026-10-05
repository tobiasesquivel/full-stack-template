using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Identity;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.Tests;

public sealed class TasksTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public TasksTests(ApiFixture fixture)
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
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (session.Cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", session.Cookies.Select(kv => $"{kv.Key}={kv.Value.Value}")));

        if (authenticated && session.AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        if (csrf && method != HttpMethod.Get && session.Cookies.TryGetValue("XSRF-TOKEN", out var xsrf))
            request.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(xsrf.Value));

        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var httpResponse = await session.Client.SendAsync(request);

        if (httpResponse.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var raw in setCookies)
            {
                var parts = raw.Split(';').Select(p => p.Trim()).ToArray();
                var pair = parts[0];
                var equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;
                var name = pair[..equals];
                var value = pair[(equals + 1)..];
                var expired = parts.Any(p => p.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase))
                    || parts.Where(p => p.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase))
                        .Any(p => DateTimeOffset.TryParse(p["Expires=".Length..], out var exp) && exp <= DateTimeOffset.UtcNow);
                if (expired || value.Length == 0)
                {
                    session.Cookies.Remove(name);
                    continue;
                }
                var path = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?["Path=".Length..] ?? "/";
                var httpOnly = parts.Any(p => p.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase));
                session.Cookies[name] = new CookieDatum { Value = value, Path = path, HttpOnly = httpOnly };
            }
        }

        var text = await httpResponse.Content.ReadAsStringAsync();
        return new Response
        {
            Status = httpResponse.StatusCode,
            Payload = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text),
        };
    }

    private async Task CsrfAsync(Session session)
    {
        var response = await SendAsync(session, HttpMethod.Get, "api/v1/auth/csrf", authenticated: false, csrf: false);
        response.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(session.Cookies.ContainsKey("XSRF-TOKEN"), "CSRF endpoint must issue XSRF-TOKEN.");
    }

    private async Task RegisterAsync(Session session, string userName)
    {
        await CsrfAsync(session);
        session.UserName = userName;
        session.Password = $"Smoke!{Guid.NewGuid():N}Aa9";

        var response = await SendAsync(
            session,
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName, email = $"{userName}@example.test", password = session.Password, displayName = userName },
            authenticated: false);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
        {
            var root = response.Payload.RootElement;
            Assert.False(root.TryGetProperty("refreshToken", out _), "Refresh token must not appear in JSON.");
            session.AccessToken = root.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrEmpty(session.AccessToken));
            var user = root.GetProperty("user");
            session.AuthId = user.GetProperty("authId").GetString()!;
            Assert.Equal(userName, user.GetProperty("userName").GetString());
        }

        Assert.Contains(session.Cookies.Values, c => c.HttpOnly && c.Path == "/api/v1/auth");
    }

    private async Task SetAdminAsync(string userName, bool admin)
    {
        using var scope = _fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        var identity = await users.FindByNameAsync(userName);
        Assert.NotNull(identity);
        var result = admin
            ? await users.AddToRoleAsync(identity, "Admin")
            : await users.RemoveFromRoleAsync(identity, "Admin");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Create_Returns201_WithTrimmedTitle()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        var response = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "  Primera tarea  " });

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
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        var blank = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "   " });
        blank.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);

        var oversized = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = new string('x', 201) });
        oversized.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, oversized.Status);
    }

    [Fact]
    public async Task List_ReturnsOwnTasks_WithPagingShape()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        var ids = new List<string>();
        foreach (var title in new[] { "Tarea uno", "Tarea dos", "Tarea tres" })
        {
            var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title });
            Assert.Equal(HttpStatusCode.Created, created.Status);
            Assert.NotNull(created.Payload);
            using (created.Payload)
                ids.Add(created.Payload.RootElement.GetProperty("id").GetString()!);
        }

        var firstPage = await SendAsync(alice, HttpMethod.Get, "api/v1/tasks?page=1&pageSize=2", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, firstPage.Status);
        Assert.NotNull(firstPage.Payload);
        List<string> firstIds;
        using (firstPage.Payload)
        {
            var page = firstPage.Payload.RootElement;
            Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, page.GetProperty("page").GetInt32());
            Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
            Assert.Equal(2, page.GetProperty("items").GetArrayLength());
            firstIds = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
        }

        var secondPage = await SendAsync(alice, HttpMethod.Get, "api/v1/tasks?page=2&pageSize=2", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, secondPage.Status);
        Assert.NotNull(secondPage.Payload);
        using (secondPage.Payload)
        {
            var page = secondPage.Payload.RootElement;
            Assert.Equal(1, page.GetProperty("items").GetArrayLength());
            var secondIds = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
            Assert.Equal(3, new HashSet<string>(firstIds.Concat(secondIds)).Count);
        }

        var tooWide = await SendAsync(alice, HttpMethod.Get, "api/v1/tasks?pageSize=101", authenticated: true, csrf: false);
        tooWide.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.Status);

        var zeroPage = await SendAsync(alice, HttpMethod.Get, "api/v1/tasks?page=0", authenticated: true, csrf: false);
        zeroPage.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, zeroPage.Status);
    }

    [Fact]
    public async Task List_IsolatesOwners()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));
        var bob = NewSession();
        await RegisterAsync(bob, UniqueName("bob"));

        var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Solo Alice" });
        created.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, created.Status);

        var bobPage = await SendAsync(bob, HttpMethod.Get, "api/v1/tasks", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, bobPage.Status);
        Assert.NotNull(bobPage.Payload);
        using (bobPage.Payload)
        {
            var page = bobPage.Payload.RootElement;
            Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task GetById_ReturnsOwn_And_Missing_Returns404()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Propia" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        string id;
        using (created.Payload)
            id = created.Payload.RootElement.GetProperty("id").GetString()!;

        var fetched = await SendAsync(alice, HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.Equal("Propia", fetched.Payload.RootElement.GetProperty("title").GetString());

        var missing = await SendAsync(alice, HttpMethod.Get, $"api/v1/tasks/{Guid.NewGuid():D}", authenticated: true, csrf: false);
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task ForeignTask_IsForbidden_Then_AdminSeesIt_Then_RevokeHidesItAgain()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));
        var bob = NewSession();
        var bobName = UniqueName("bob");
        await RegisterAsync(bob, bobName);

        var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Ajena" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        string id;
        using (created.Payload)
            id = created.Payload.RootElement.GetProperty("id").GetString()!;

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            object? body = method == HttpMethod.Put ? new { title = "Ajena", isCompleted = true } : null;
            var forbidden = await SendAsync(bob, method, $"api/v1/tasks/{id}", body);
            forbidden.Payload?.Dispose();
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);
        }

        await SetAdminAsync(bobName, true);

        var asAdmin = await SendAsync(bob, HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, asAdmin.Status);
        asAdmin.Payload?.Dispose();

        var adminUpdate = await SendAsync(bob, HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Editada por admin", isCompleted = false });
        Assert.Equal(HttpStatusCode.OK, adminUpdate.Status);
        adminUpdate.Payload?.Dispose();

        await SetAdminAsync(bobName, false);

        var afterRevoke = await SendAsync(bob, HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        afterRevoke.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.Status);
    }

    [Fact]
    public async Task Update_PersistsFinalState_Idempotently()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Borrador" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        string id;
        using (created.Payload)
            id = created.Payload.RootElement.GetProperty("id").GetString()!;

        var updated = await SendAsync(alice, HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.NotNull(updated.Payload);
        using (updated.Payload)
        {
            Assert.Equal("Estado final", updated.Payload.RootElement.GetProperty("title").GetString());
            Assert.True(updated.Payload.RootElement.GetProperty("isCompleted").GetBoolean());
        }

        var repeated = await SendAsync(alice, HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "Estado final", isCompleted = true });
        Assert.Equal(HttpStatusCode.OK, repeated.Status);
        repeated.Payload?.Dispose();

        var fetched = await SendAsync(alice, HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, fetched.Status);
        Assert.NotNull(fetched.Payload);
        using (fetched.Payload)
            Assert.True(fetched.Payload.RootElement.GetProperty("isCompleted").GetBoolean());

        var invalid = await SendAsync(alice, HttpMethod.Put, $"api/v1/tasks/{id}", new { title = "", isCompleted = false });
        invalid.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        var missing = await SendAsync(alice, HttpMethod.Put, $"api/v1/tasks/{Guid.NewGuid():D}", new { title = "x", isCompleted = false });
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Delete_RemovesOwn_Then_Returns404()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Temporal" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        string id;
        using (created.Payload)
            id = created.Payload.RootElement.GetProperty("id").GetString()!;

        var deleted = await SendAsync(alice, HttpMethod.Delete, $"api/v1/tasks/{id}");
        deleted.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        var gone = await SendAsync(alice, HttpMethod.Get, $"api/v1/tasks/{id}", authenticated: true, csrf: false);
        gone.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, gone.Status);

        var missing = await SendAsync(alice, HttpMethod.Delete, $"api/v1/tasks/{Guid.NewGuid():D}");
        missing.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    [Fact]
    public async Task Delete_Foreign_Returns403_While_AdminDeleteSucceeds()
    {
        var alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));
        var bob = NewSession();
        var bobName = UniqueName("bob");
        await RegisterAsync(bob, bobName);

        var created = await SendAsync(alice, HttpMethod.Post, "api/v1/tasks", new { title = "Protegida" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.NotNull(created.Payload);
        string id;
        using (created.Payload)
            id = created.Payload.RootElement.GetProperty("id").GetString()!;

        var forbidden = await SendAsync(bob, HttpMethod.Delete, $"api/v1/tasks/{id}");
        forbidden.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.Status);

        await SetAdminAsync(bobName, true);

        var adminDelete = await SendAsync(bob, HttpMethod.Delete, $"api/v1/tasks/{id}");
        adminDelete.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, adminDelete.Status);

        await SetAdminAsync(bobName, false);
    }
}
