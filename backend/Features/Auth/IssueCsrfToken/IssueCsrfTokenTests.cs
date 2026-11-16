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
            csrf: false);

        without.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, without.Status);

        await anonymous.CsrfAsync();
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("logout")]
    public async Task AuthPost_WithoutCsrf_Returns400_WithCode_And_SessionSurvives(string route)
    {
        TestSession alice = new(_fixture.CreateClient());
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);
        string sessionBefore = alice.Cookies[TestAccounts.SessionCookieName].Value;

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
            csrf: false);

        AssertAntiforgeryFailed(rejected);
        Assert.Equal(sessionBefore, alice.Cookies[TestAccounts.SessionCookieName].Value);

        SessionResponse me = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        me.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, me.Status);
    }

    [Theory]
    [InlineData("POST", "api/v1/tasks")]
    [InlineData("PUT", "api/v1/tasks/{task}")]
    [InlineData("DELETE", "api/v1/tasks/{task}")]
    [InlineData("PUT", "api/v1/users/me")]
    [InlineData("DELETE", "api/v1/users/me")]
    public async Task ApiWrite_WithSessionButWithoutCsrf_Returns400_WithCode_And_ChangesNothing(string method, string route)
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        string task = await alice.CreateTaskAsync("Intacta");
        uint version = await alice.TaskVersionAsync(task);

        object? body = (method, route) switch
        {
            ("POST", _) => new { title = "Sin token" },
            ("PUT", "api/v1/users/me") => new { displayName = "Sin token", timezone = "UTC" },
            ("PUT", _) => new { title = "Sin token", isCompleted = true, version },
            _ => null,
        };
        SessionResponse rejected = await alice.SendAsync(
            new HttpMethod(method),
            route.Replace("{task}", task, StringComparison.Ordinal),
            body,
            csrf: false);

        AssertAntiforgeryFailed(rejected);

        SessionResponse kept = await alice.SendAsync(HttpMethod.Get, $"api/v1/tasks/{task}", csrf: false);
        Assert.Equal(HttpStatusCode.OK, kept.Status);
        Assert.NotNull(kept.Payload);
        using (kept.Payload)
            Assert.Equal("Intacta", kept.Payload.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Register_RotatesTheCsrfToken_And_TheNewOneAcceptsWrites()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.CsrfAsync();
        string anonymousToken = alice.Cookies[TestAccounts.XsrfCookieName].Value;
        string name = TestAccounts.UniqueName("alice");
        SessionResponse registered = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = name });
        registered.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, registered.Status);
        Assert.NotEqual(anonymousToken, alice.Cookies[TestAccounts.XsrfCookieName].Value);

        SessionResponse rotated = await alice.SendAsync(HttpMethod.Post, "api/v1/tasks", new { title = "Con token nuevo" });
        rotated.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Created, rotated.Status);
    }

    private static void AssertAntiforgeryFailed(SessionResponse rejected)
    {
        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
        Assert.NotNull(rejected.Payload);
        using (rejected.Payload)
            Assert.Equal("Auth.AntiforgeryFailed", rejected.Payload.RootElement.GetProperty("code").GetString());
    }
}
