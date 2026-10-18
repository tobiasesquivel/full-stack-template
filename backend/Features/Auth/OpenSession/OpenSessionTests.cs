using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.OpenSession;

public sealed class OpenSessionTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public OpenSessionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_RejectsBadCredentials_Then_AcceptsValid()
    {
        TestSession alice = new(_fixture.CreateClient());
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);

        TestSession attempt = new(_fixture.CreateClient());
        await attempt.CsrfAsync();
        SessionResponse bad = await attempt.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = name, password = "wrong-password" });

        bad.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);

        TestSession missing = new(_fixture.CreateClient());
        await missing.CsrfAsync();
        SessionResponse unknown = await missing.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = TestAccounts.UniqueName("ghost"), password = "wrong-password" });

        unknown.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);

        TestSession login = new(_fixture.CreateClient());
        await login.CsrfAsync();
        SessionResponse ok = await login.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = name, password = alice.Password });

        Assert.Equal(HttpStatusCode.OK, ok.Status);
        TestAccounts.AssertCarriesNoToken(ok);
        Assert.NotNull(ok.Payload);
        using (ok.Payload)
        {
            JsonElement user = ok.Payload.RootElement;
            Assert.Equal(alice.AuthId, user.GetProperty("authId").GetString());
            Assert.Equal(name, user.GetProperty("userName").GetString());
        }
        TestAccounts.AssertSessionCookie(login);
        Assert.Equal(
            [TestAccounts.XsrfCookieName],
            login.Cookies.Where(c => !c.Value.HttpOnly).Select(c => c.Key).ToArray());

        SessionResponse me = await login.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        me.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, me.Status);
    }

    [Fact]
    public async Task Login_WithBadCredentials_SetsNoSessionCookie()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        TestSession attempt = new(_fixture.CreateClient());
        await attempt.CsrfAsync();
        SessionResponse bad = await attempt.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = "wrong-password" });

        bad.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);
        Assert.False(attempt.Cookies.ContainsKey(TestAccounts.SessionCookieName));
    }
}
