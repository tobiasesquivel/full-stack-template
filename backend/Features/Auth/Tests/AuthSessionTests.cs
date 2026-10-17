using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.Tests;

public sealed class AuthSessionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public AuthSessionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    private TestSession NewSession() => new(_fixture.CreateClient());

    private TestSession NewIsolatedSession() => new(_fixture.CreateIsolatedClient(out _));
    private static string UniqueName(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 16, prefix.Length + 32)];

    private async Task CsrfAsync(TestSession session)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, "api/v1/auth/csrf", authenticated: false, csrf: false);
        response.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(session.Cookies.ContainsKey("XSRF-TOKEN"), "CSRF endpoint must issue XSRF-TOKEN.");
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
        TestSession anonymous = NewSession();
        SessionResponse without = await anonymous.SendAsync(
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
        TestSession alice = NewSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);
        AssertJwtIsIdentityOnly(alice.AccessToken!);

        TestSession rival = NewSession();
        await CsrfAsync(rival);
        SessionResponse duplicate = await rival.SendAsync(
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
        TestSession alice = NewSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);

        TestSession attempt = NewSession();
        await CsrfAsync(attempt);
        SessionResponse bad = await attempt.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = name, password = "wrong-password" },
            authenticated: false);

        bad.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);

        TestSession missing = NewSession();
        await CsrfAsync(missing);
        SessionResponse unknown = await missing.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = UniqueName("ghost"), password = "wrong-password" },
            authenticated: false);

        unknown.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);

        TestSession login = NewSession();
        await CsrfAsync(login);
        SessionResponse ok = await login.SendAsync(
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
        TestSession alice = NewIsolatedSession();
        await RegisterAsync(alice, UniqueName("alice"));

        TestSession second = NewIsolatedSession();
        await CsrfAsync(second);
        SessionResponse relogin = await second.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        using (relogin.Payload!)
            second.AccessToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();

        TestSession racerB = NewIsolatedSession();
        await CsrfAsync(racerB);
        racerB.Cookies["template_refresh"] = new SessionCookie { Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value, Path = "/api/v1/auth", HttpOnly = true };
        TestSession racerA = NewIsolatedSession();
        await CsrfAsync(racerA);
        racerA.Cookies["template_refresh"] = new SessionCookie { Value = racerB.Cookies["template_refresh"].Value, Path = "/api/v1/auth", HttpOnly = true };
        string sharedRefresh = racerA.Cookies["template_refresh"].Value;

        Task<SessionResponse> left = racerA.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Task<SessionResponse> right = racerB.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        SessionResponse[] raced = await Task.WhenAll(left, right);

        Assert.Contains(raced, r => r.Status == HttpStatusCode.OK);
        Assert.Contains(raced, r => r.Status == HttpStatusCode.Unauthorized);
        string? winnerAccess = null;
        foreach (SessionResponse r in raced)
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

        TestSession winnerProbe = NewIsolatedSession();
        winnerProbe.AccessToken = winnerAccess;
        SessionResponse winnerMe = await winnerProbe.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        winnerMe.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, winnerMe.Status);

        TestSession probe = NewIsolatedSession();
        await CsrfAsync(probe);
        probe.Cookies["template_refresh"] = new SessionCookie { Value = sharedRefresh, Path = "/api/v1/auth", HttpOnly = true };
        SessionResponse after = await probe.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        after.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, after.Status);

        SessionResponse survivor = await second.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        survivor.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, survivor.Status);
    }

    [Fact]
    public async Task Refresh_TwoConsecutiveRotationsSucceed_Then_ReplayRevokesFamily_And_OtherSessionSurvives()
    {
        TestSession alice = NewIsolatedSession();
        await RegisterAsync(alice, UniqueName("alice"));

        TestSession second = NewIsolatedSession();
        await CsrfAsync(second);
        SessionResponse relogin = await second.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        using (relogin.Payload!)
            second.AccessToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();

        SessionCookie firstStale = new()
        {
            Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value,
            Path = "/api/v1/auth",
            HttpOnly = true,
        };

        SessionResponse first = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        using (first.Payload!)
        {
            alice.AccessToken = first.Payload.RootElement.GetProperty("accessToken").GetString();
            Assert.False(first.Payload.RootElement.TryGetProperty("refreshToken", out _));
        }
        AssertJwtIsIdentityOnly(alice.AccessToken!);

        SessionResponse meAfterFirst = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        meAfterFirst.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, meAfterFirst.Status);

        SessionCookie secondStale = new()
        {
            Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value,
            Path = "/api/v1/auth",
            HttpOnly = true,
        };
        Assert.NotEqual(firstStale.Value, secondStale.Value);

        SessionResponse secondRefresh = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Assert.Equal(HttpStatusCode.OK, secondRefresh.Status);
        using (secondRefresh.Payload!)
        {
            alice.AccessToken = secondRefresh.Payload.RootElement.GetProperty("accessToken").GetString();
            Assert.False(secondRefresh.Payload.RootElement.TryGetProperty("refreshToken", out _));
        }
        AssertJwtIsIdentityOnly(alice.AccessToken!);
        Assert.NotEqual(secondStale.Value, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        SessionResponse meAfterSecond = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        meAfterSecond.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, meAfterSecond.Status);

        TestSession replay = NewIsolatedSession();
        await CsrfAsync(replay);
        replay.Cookies["template_refresh"] = secondStale;

        SessionResponse replayed = await replay.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.Status);

        SessionResponse revoked = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        revoked.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.Status);

        SessionResponse survivor = await second.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        survivor.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, survivor.Status);

        TestSession replayFirst = NewIsolatedSession();
        await CsrfAsync(replayFirst);
        replayFirst.Cookies["template_refresh"] = firstStale;

        SessionResponse replayedFirst = await replayFirst.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayedFirst.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayedFirst.Status);
    }

    [Fact]
    public async Task Refresh_Rotates_Then_ReplayRevokesFamily_And_OtherSessionSurvives()
    {
        TestSession alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        TestSession second = NewSession();
        await CsrfAsync(second);
        SessionResponse relogin = await second.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        string? secondToken;
        using (relogin.Payload!)
            secondToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();
        second.AccessToken = secondToken;

        SessionCookie stale = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth");
        SessionCookie staleCopy = new() { Value = stale.Value, Path = stale.Path, HttpOnly = stale.HttpOnly };

        SessionResponse fresh = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        Assert.Equal(HttpStatusCode.OK, fresh.Status);
        Assert.NotNull(fresh.Payload);
        using (fresh.Payload)
        {
            alice.AccessToken = fresh.Payload.RootElement.GetProperty("accessToken").GetString();
            Assert.False(fresh.Payload.RootElement.TryGetProperty("refreshToken", out _));
        }
        AssertJwtIsIdentityOnly(alice.AccessToken!);
        Assert.NotEqual(staleCopy.Value, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        TestSession replay = NewSession();
        foreach (KeyValuePair<string, SessionCookie> kv in alice.Cookies)
            replay.Cookies[kv.Key] = new SessionCookie { Value = kv.Value.Value, Path = kv.Value.Path, HttpOnly = kv.Value.HttpOnly };
        replay.Cookies["template_refresh"] = staleCopy;

        SessionResponse replayed = await replay.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayed.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.Status);

        SessionResponse revoked = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        revoked.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.Status);

        SessionResponse survivor = await second.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        Assert.Equal(HttpStatusCode.OK, survivor.Status);
        survivor.Payload?.Dispose();
    }

    [Fact]
    public async Task Logout_IsIdempotent_And_InvalidatesAccess()
    {
        TestSession alice = NewSession();
        await RegisterAsync(alice, UniqueName("alice"));

        string? revokedAccess = alice.AccessToken;

        SessionResponse first = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/logout", body: null, authenticated: false);
        first.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, first.Status);

        SessionResponse repeated = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/logout", body: null, authenticated: false);
        repeated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, repeated.Status);

        alice.AccessToken = revokedAccess;
        SessionResponse denied = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task Post_WithoutCsrf_Returns400_WithCode_And_SessionSurvives(string route)
    {
        TestSession alice = NewIsolatedSession();
        string name = UniqueName("alice");
        await RegisterAsync(alice, name);
        string refreshBefore = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value;

        object? body = route switch
        {
            "register" => new
            {
                userName = UniqueName("rival"),
                email = $"{UniqueName("rival")}@example.test",
                password = $"Smoke!{Guid.NewGuid():N}Aa9",
                displayName = "Rival",
            },
            "login" => new { userName = alice.UserName, password = alice.Password },
            _ => null,
        };
        SessionResponse rejected = await alice.SendAsync(
            HttpMethod.Post,
            $"api/v1/auth/{route}",
            body,
            authenticated: false,
            csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
        Assert.NotNull(rejected.Payload);
        using (rejected.Payload)
            Assert.Equal("Auth.AntiforgeryFailed", rejected.Payload.RootElement.GetProperty("code").GetString());

        Assert.Equal(refreshBefore, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        SessionResponse me = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        me.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, me.Status);
    }
}
