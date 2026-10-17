using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.IssueCsrfToken;

public sealed class IssueCsrfTokenTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public IssueCsrfTokenTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Csrf_IssuesToken_Then_MutationsWithoutIt_Return400()
    {
        TestSession anonymous = new(_fixture.CreateClient());
        SessionResponse without = await anonymous.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = "nobody", password = "invalid" },
            authenticated: false,
            csrf: false);

        without.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, without.Status);

        await anonymous.CsrfAsync();
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task Post_WithoutCsrf_Returns400_WithCode_And_SessionSurvives(string route)
    {
        TestSession alice = new(_fixture.CreateIsolatedClient(out _));
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);
        string refreshBefore = alice.Cookies.Values.Single(c => c.HttpOnly && c.Path == "/api/v1/auth").Value;

        object? body = route switch
        {
            "register" => new
            {
                userName = TestAccounts.UniqueName("rival"),
                email = $"{TestAccounts.UniqueName("rival")}@example.test",
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
