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
            new { userName = name, password = "wrong-password" },
            authenticated: false);

        bad.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);

        TestSession missing = new(_fixture.CreateClient());
        await missing.CsrfAsync();
        SessionResponse unknown = await missing.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = TestAccounts.UniqueName("ghost"), password = "wrong-password" },
            authenticated: false);

        unknown.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);

        TestSession login = new(_fixture.CreateClient());
        await login.CsrfAsync();
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
}
