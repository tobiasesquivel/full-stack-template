using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.CloseSession;

public sealed class CloseSessionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CloseSessionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Logout_IsIdempotent_ClearsTheCookie_And_TheSameCookieGets401()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession stolen = alice.CopyTo(_fixture.CreateClient());

        SessionResponse first = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/logout");
        first.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, first.Status);
        Assert.False(alice.Cookies.ContainsKey(TestAccounts.SessionCookieName), "Logout must clear the session cookie.");

        await alice.CsrfAsync();
        SessionResponse repeated = await alice.SendAsync(HttpMethod.Post, "api/v1/auth/logout");
        repeated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, repeated.Status);

        SessionResponse denied = await stolen.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, denied.Status);
    }

    [Fact]
    public async Task DeletingTheSessionRow_CutsAccess_OnTheNextRequest_And_OtherSessionSurvives()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession laptop = alice.CopyTo(_fixture.CreateClient());
        laptop.Cookies.Clear();
        SessionResponse opened = await laptop.OpenAsync();
        opened.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, opened.Status);

        SessionResponse before = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        before.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, before.Status);

        int deleted = await _fixture.SqlAsync(
            $"DELETE FROM \"Sessions\" WHERE \"UserId\" = {alice.AuthId} AND \"CreatedAt\" = (SELECT MIN(\"CreatedAt\") FROM \"Sessions\" WHERE \"UserId\" = {alice.AuthId})");
        Assert.Equal(1, deleted);

        SessionResponse after = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        after.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, after.Status);

        SessionResponse survivor = await laptop.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        survivor.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, survivor.Status);
    }

    [Fact]
    public async Task ExpiredOrIdleSession_Gets401()
    {
        TestSession expired = new(_fixture.CreateClient());
        await expired.RegisterAsync(TestAccounts.UniqueName("expired"));
        await _fixture.SqlAsync($"UPDATE \"Sessions\" SET \"ExpiresAt\" = now() - interval '1 second' WHERE \"UserId\" = {expired.AuthId}");

        SessionResponse afterExpiry = await expired.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        afterExpiry.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.Status);

        TestSession idle = new(_fixture.CreateClient());
        await idle.RegisterAsync(TestAccounts.UniqueName("idle"));
        await _fixture.SqlAsync($"UPDATE \"Sessions\" SET \"LastSeenAt\" = now() - interval '1 day' WHERE \"UserId\" = {idle.AuthId}");

        SessionResponse afterIdle = await idle.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        afterIdle.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, afterIdle.Status);
    }

    [Fact]
    public async Task LastSeen_MovesAtMostOncePerMinute()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        await _fixture.SqlAsync($"UPDATE \"Sessions\" SET \"LastSeenAt\" = now() - interval '2 minutes' WHERE \"UserId\" = {alice.AuthId}");
        DateTimeOffset stale = Assert.Single(await _fixture.LastSeenAsync(alice.AuthId));

        SessionResponse first = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        first.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, first.Status);
        DateTimeOffset touched = Assert.Single(await _fixture.LastSeenAsync(alice.AuthId));
        Assert.True(touched > stale.AddMinutes(1), "The first request after a minute must move LastSeenAt.");

        SessionResponse second = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        second.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(touched, Assert.Single(await _fixture.LastSeenAsync(alice.AuthId)));
    }
}
