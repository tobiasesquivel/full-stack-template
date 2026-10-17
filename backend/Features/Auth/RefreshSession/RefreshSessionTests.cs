using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.RefreshSession;

public sealed class RefreshSessionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public RefreshSessionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Refresh_ConcurrentSameToken_OnlyOneRotates_And_FamilyRevoked_OtherSessionSurvives()
    {
        TestSession alice = new(_fixture.CreateIsolatedClient(out _));
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        TestSession second = new(_fixture.CreateIsolatedClient(out _));
        await second.CsrfAsync();
        SessionResponse relogin = await second.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password },
            authenticated: false);
        Assert.Equal(HttpStatusCode.OK, relogin.Status);
        using (relogin.Payload!)
            second.AccessToken = relogin.Payload.RootElement.GetProperty("accessToken").GetString();

        TestSession racerB = new(_fixture.CreateIsolatedClient(out _));
        await racerB.CsrfAsync();
        racerB.Cookies["template_refresh"] = new SessionCookie { Value = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value, Path = "/api/v1/auth", HttpOnly = true };
        TestSession racerA = new(_fixture.CreateIsolatedClient(out _));
        await racerA.CsrfAsync();
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

        TestSession winnerProbe = new(_fixture.CreateIsolatedClient(out _));
        winnerProbe.AccessToken = winnerAccess;
        SessionResponse winnerMe = await winnerProbe.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        winnerMe.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, winnerMe.Status);

        TestSession probe = new(_fixture.CreateIsolatedClient(out _));
        await probe.CsrfAsync();
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
        TestSession alice = new(_fixture.CreateIsolatedClient(out _));
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        TestSession second = new(_fixture.CreateIsolatedClient(out _));
        await second.CsrfAsync();
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
        TestAccounts.AssertJwtIsIdentityOnly(alice.AccessToken!);

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
        TestAccounts.AssertJwtIsIdentityOnly(alice.AccessToken!);
        Assert.NotEqual(secondStale.Value, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        SessionResponse meAfterSecond = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", authenticated: true, csrf: false);
        meAfterSecond.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, meAfterSecond.Status);

        TestSession replay = new(_fixture.CreateIsolatedClient(out _));
        await replay.CsrfAsync();
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

        TestSession replayFirst = new(_fixture.CreateIsolatedClient(out _));
        await replayFirst.CsrfAsync();
        replayFirst.Cookies["template_refresh"] = firstStale;

        SessionResponse replayedFirst = await replayFirst.SendAsync(HttpMethod.Post, "api/v1/auth/refresh", body: null, authenticated: false);
        replayedFirst.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, replayedFirst.Status);
    }

    [Fact]
    public async Task Refresh_Rotates_Then_ReplayRevokesFamily_And_OtherSessionSurvives()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        TestSession second = new(_fixture.CreateClient());
        await second.CsrfAsync();
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
        TestAccounts.AssertJwtIsIdentityOnly(alice.AccessToken!);
        Assert.NotEqual(staleCopy.Value, alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value);

        TestSession replay = new(_fixture.CreateClient());
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
}
