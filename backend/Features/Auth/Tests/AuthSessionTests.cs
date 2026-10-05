using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Identity;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.Tests;

public sealed class AuthSessionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AuthSessionTests(ApiFixture fixture)
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

    private Session NewIsolatedSession() => new() { Client = _fixture.CreateIsolatedClient(out _) };
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
        Assert.True(session.Cookies.ContainsKey("XSRF-TOKEN"), "CSRF endpoint must issue XSRF-TOKEN.");
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

    private static void AssertJwtIsIdentityOnly(string accessToken)
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

    [Fact]
    public async Task Csrf_IssuesToken_Then_MutationsWithoutIt_Return400()
    {
        Session anonymous = NewSession();
        Response without = await SendAsync(
            anonymous,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = "nobody", password = "invalid" },
            authenticated: false,
            csrf: false);

        without.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, without.Status);
    }

    [Fact]
    public async Task Register_Returns201_WithSession_Then_Duplicate_Returns409_WithoutPartialRows()
    {
        Session alice = NewSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);
        AssertJwtIsIdentityOnly(alice.AccessToken!);

        Session rival = NewSession();
        await CsrfAsync(rival);
        Response duplicate = await SendAsync(
            rival,
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = name },
            authenticated: false);

        duplicate.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);

        using IServiceScope scope = _fixture.Services.CreateScope();
        UserManager<AppIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        AppIdentityUser? identity = await users.FindByNameAsync(name);
        Assert.NotNull(identity);
    }

    [Fact]
    public async Task Login_RejectsBadCredentials_Then_AcceptsValid()
    {
        Session alice = NewSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);

        Session attempt = NewSession();
        await CsrfAsync(attempt);
        Response bad = await SendAsync(
            attempt,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = name, password = "wrong-password" },
            authenticated: false);

        bad.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);

        Session missing = NewSession();
        await CsrfAsync(missing);
        Response unknown = await SendAsync(
            missing,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = UniqueName("ghost"), password = "wrong-password" },
            authenticated: false);

        unknown.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);

        Session login = NewSession();
        await CsrfAsync(login);
        Response ok = await SendAsync(
            login,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = name, password = alice.Password },
            authenticated: false);

        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.NotNull(ok.Payload);
        using (ok.Payload)
        {
            JsonElement root = ok.Payload.RootElement;
            Assert.False(string.IsNullOrEmpty(root.GetProperty("accessToken").GetString()));
            Assert.False(root.TryGetProperty("refreshToken", out _));
        }
        Assert.Contains(login.Cookies.Values, c => c.HttpOnly && c.Path == "/api/v1/auth");
    }

    [Fact]
    public async Task Refresh_ConcurrentSameToken_OnlyOneRotates_And_FamilyRevoked_OtherSessionSurvives()
    {
        // Fábrica propia: el limiter "auth" es por app (20/min). Los dos tests nuevos
        // consumen ~10 permisos cada uno; en la fábrica compartida agotan el cupo y
        // contaminan al resto de la suite con 503. Misma DB real, sin tocar la política.
        Session alice = NewIsolatedSession();
        await RegisterAsync(alice, UniqueName("alice"));

        Session second = NewIsolatedSession();
        await CsrfAsync(second);
        Response relogin = await SendAsync(
            second,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        using (relogin.Payload!)
            second.AccessToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();

        // Dos redenciones concurrentes del mismo token vigente (scenario Dos redenciones):
        // sesiones independientes con XSRF propio (patrón SendAsync existente) que
        // comparten solo el valor de la cookie de refresh, como dos pestañas.
        Session racerB = NewIsolatedSession();
        await CsrfAsync(racerB);
        racerB.Cookies["template_refresh"] = new CookieDatum { Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value, Path = "/api/v1/auth", HttpOnly = true };
        Session racerA = NewIsolatedSession();
        await CsrfAsync(racerA);
        racerA.Cookies["template_refresh"] = new CookieDatum { Value = racerB.Cookies["template_refresh"].Value, Path = "/api/v1/auth", HttpOnly = true };
        string sharedRefresh = racerA.Cookies["template_refresh"].Value;

        Task<Response> left = SendAsync(racerA, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Task<Response> right = SendAsync(racerB, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Response[] raced = await Task.WhenAll(left, right);

        // Solo una rota (200); el reuso es replay y revoca la familia (401).
        Assert.Contains(raced, r => r.Status == HttpStatusCode.OK);
        Assert.Contains(raced, r => r.Status == HttpStatusCode.Unauthorized);
        string? winnerAccess = null;
        foreach (Response r in raced)
        {
            if (r.Status == HttpStatusCode.OK)
            {
                Assert.NotNull(r.Payload);
                using (r.Payload)
                    winnerAccess = r.Payload.RootElement.GetProperty("accessToken").GetString();
            }
            else
            {
                r.Payload?.Dispose();
            }
        }
        Assert.False(string.IsNullOrEmpty(winnerAccess));

        // El access del ganador muere con la familia: el replay del perdedor la
        // revocó (sid revocable, sin gracia hasta expiración).
        Session winnerProbe = NewIsolatedSession();
        winnerProbe.AccessToken = winnerAccess;
        Response winnerMe = await SendAsync(winnerProbe, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        winnerMe.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, winnerMe.Status);

        // Cualquier token restante de la familia también cae.
        Session probe = NewIsolatedSession();
        await CsrfAsync(probe);
        probe.Cookies["template_refresh"] = new CookieDatum { Value = sharedRefresh, Path = "/api/v1/auth", HttpOnly = true };
        Response after = await SendAsync(probe, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        after.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, after.Status);

        Response survivor = await SendAsync(second, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        survivor.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, survivor.Status);
    }

    [Fact]
    public async Task Refresh_TwoConsecutiveRotationsSucceed_Then_ReplayRevokesFamily_And_OtherSessionSurvives()
    {
        Session alice = NewIsolatedSession();
        await RegisterAsync(alice, UniqueName("alice"));

        Session second = NewIsolatedSession();
        await CsrfAsync(second);
        Response relogin = await SendAsync(
            second,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        using (relogin.Payload!)
            second.AccessToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();

        CookieDatum firstStale = new()
        {
            Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value,
            Path = "/api/v1/auth",
            HttpOnly = true,
        };

        Response first = await SendAsync(alice, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        using (first.Payload!)
        {
            alice.AccessToken = first.Payload.RootElement.GetProperty("accessToken").GetString();
            Assert.False(first.Payload.RootElement.TryGetProperty("refreshToken", out _));
        }
        AssertJwtIsIdentityOnly(alice.AccessToken!);

        Response meAfterFirst = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        meAfterFirst.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, meAfterFirst.Status);

        CookieDatum secondStale = new()
        {
            Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value,
            Path = "/api/v1/auth",
            HttpOnly = true,
        };
        Assert.NotEqual(firstStale.Value, secondStale.Value);

        Response secondRefresh = await SendAsync(alice, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Assert.Equal(HttpStatusCode.OK, secondRefresh.Status);
        using (secondRefresh.Payload!)
        {
            alice.AccessToken = secondRefresh.Payload.RootElement.GetProperty("accessToken").GetString();
            Assert.False(secondRefresh.Payload.RootElement.TryGetProperty("refreshToken", out _));
        }
        AssertJwtIsIdentityOnly(alice.AccessToken!);
        Assert.NotEqual(secondStale.Value, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        Response meAfterSecond = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        meAfterSecond.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, meAfterSecond.Status);

        // Replay con XSRF propio (no se copia el ajeno): solo se comparte el refresh.
        Session replay = NewIsolatedSession();
        await CsrfAsync(replay);
        replay.Cookies["template_refresh"] = secondStale;

        Response replayed = await SendAsync(replay, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.Status);

        Response revoked = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        revoked.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.Status);

        Response survivor = await SendAsync(second, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        survivor.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, survivor.Status);

        Session replayFirst = NewIsolatedSession();
        await CsrfAsync(replayFirst);
        replayFirst.Cookies["template_refresh"] = firstStale;

        Response replayedFirst = await SendAsync(replayFirst, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayedFirst.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayedFirst.Status);
    }

    [Fact]
    public async Task Refresh_Rotates_Then_ReplayRevokesFamily_And_OtherSessionSurvives()
    {
        Session alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        Session second = NewSession();
        await CsrfAsync(second);
        Response relogin = await SendAsync(
            second,
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        string? secondToken;
        using (relogin.Payload!)
            secondToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();
        second.AccessToken = secondToken;

        CookieDatum stale = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth");
        CookieDatum staleCopy = new() { Value = stale.Value, Path = stale.Path, HttpOnly = stale.HttpOnly };

        Response fresh = await SendAsync(alice, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Assert.Equal(HttpStatusCode.OK, fresh.Status);
        Assert.NotNull(fresh.Payload);
        using (fresh.Payload)
        {
            alice.AccessToken = fresh.Payload.RootElement.GetProperty("accessToken").GetString();
            Assert.False(fresh.Payload.RootElement.TryGetProperty("refreshToken", out _));
        }
        AssertJwtIsIdentityOnly(alice.AccessToken!);
        Assert.NotEqual(staleCopy.Value, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        Session replay = NewSession();
        foreach (KeyValuePair<string, CookieDatum> kv in alice.Cookies)
            replay.Cookies[kv.Key] = new CookieDatum { Value = kv.Value.Value, Path = kv.Value.Path, HttpOnly = kv.Value.HttpOnly };
        replay.Cookies["template_refresh"] = staleCopy;

        Response replayed = await SendAsync(replay, HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.Status);

        Response revoked = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        revoked.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.Status);

        Response survivor = await SendAsync(second, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, survivor.Status);
        survivor.Payload?.Dispose();
    }

    [Fact]
    public async Task Logout_IsIdempotent_And_InvalidatesAccess()
    {
        Session alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        string? revokedAccess = alice.AccessToken;

        Response first = await SendAsync(alice, HttpMethod.Post, "api/v1/auth/logout", body: null, authenticated: false);
        first.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, first.Status);

        Response repeated = await SendAsync(alice, HttpMethod.Post, "api/v1/auth/logout", body: null, authenticated: false);
        repeated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, repeated.Status);

        alice.AccessToken = revokedAccess;
        Response denied = await SendAsync(alice, HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);
    }
}
