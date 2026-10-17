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
    public async Task Logout_IsIdempotent_And_InvalidatesAccess()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

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
}
