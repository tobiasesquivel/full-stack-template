using System.Net;
using System.Text.Json;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Probes;
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
    public async Task Login_CountsEachAttemptByOutcome()
    {
        HttpClient client = _fixture.CreateIsolatedClient(
            new Dictionary<string, string> { ["AuthRateLimiting:PermitLimit"] = "100" },
            out IServiceProvider services);
        TestSession alice = new(client);
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);
        using RecordedMeasurements attempts = new(services, "TemplateApp.Auth", "templateapp.auth.login.attempts");

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(client, TestAccounts.UniqueName("ghost"), "wrong-password"));
        Assert.Equal(HttpStatusCode.OK, await LoginAsync(client, name, alice.Password));
        for (int attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(client, name, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(client, name, alice.Password));

        Assert.All(attempts.Values, measurement =>
        {
            Assert.Equal(1, measurement.Value);
            Assert.Equal([Outcome], measurement.Tags.Keys);
        });
        Assert.Equal(
            ["invalid_credentials", "success", "invalid_credentials", "invalid_credentials", "invalid_credentials", "invalid_credentials", "invalid_credentials", "locked_out"],
            attempts.Values.Select(measurement => (string?)measurement.Tags[Outcome]).ToArray());
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

    private const string Outcome = "templateapp.auth.login.outcome";

    private static async Task<HttpStatusCode> LoginAsync(HttpClient client, string userName, string password)
    {
        TestSession attempt = new(client);
        await attempt.CsrfAsync();
        SessionResponse response = await attempt.SendAsync(HttpMethod.Post, "api/v1/auth/login", new { userName, password });
        response.Payload?.Dispose();
        return response.Status;
    }
}
