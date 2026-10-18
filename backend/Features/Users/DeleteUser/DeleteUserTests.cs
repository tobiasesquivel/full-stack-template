using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.DeleteUser;

public sealed class DeleteUserTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public DeleteUserTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeleteMe_Cascades_And_AccessBecomes401()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        await alice.CreateTaskAsync("Para cascada");

        SessionResponse deleted = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");
        deleted.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);

        SessionResponse denied = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        denied.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);

        await alice.CsrfAsync();
        SessionResponse relogin = await alice.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/login",
            new { userName = alice.UserName, password = alice.Password });

        relogin.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.Status);
    }
}
