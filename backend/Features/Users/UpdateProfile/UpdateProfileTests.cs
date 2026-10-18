using System.Net;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.UpdateProfile;

public sealed class UpdateProfileTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UpdateProfileTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpdateMe_Persists_DisplayName_And_Timezone_Then_RejectsInvalidZone()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse updated = await alice.SendAsync(
            HttpMethod.Put,
            "api/v1/users/me",
            new { displayName = "Perfil de smoke", timezone = "America/Argentina/Buenos_Aires" });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.NotNull(updated.Payload);
        using (updated.Payload)
        {
            Assert.Equal("Perfil de smoke", updated.Payload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("America/Argentina/Buenos_Aires", updated.Payload.RootElement.GetProperty("timezone").GetString());
        }

        SessionResponse reread = await alice.SendAsync(HttpMethod.Get, "api/v1/users/me", csrf: false);
        Assert.Equal(HttpStatusCode.OK, reread.Status);
        Assert.NotNull(reread.Payload);
        using (reread.Payload)
        {
            Assert.Equal("Perfil de smoke", reread.Payload.RootElement.GetProperty("displayName").GetString());
            Assert.Equal(alice.UserName, reread.Payload.RootElement.GetProperty("userName").GetString());
        }

        SessionResponse invalid = await alice.SendAsync(
            HttpMethod.Put,
            "api/v1/users/me",
            new { displayName = "Perfil", timezone = "No/Existe" });

        invalid.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
    }
}
