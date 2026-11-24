using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
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

    [Fact]
    public async Task UpdateMe_WhenItFailsAfterTheProfileWasSaved_KeepsTheProfileAsItWas()
    {
        HttpClient client = _fixture.CreateIsolatedClient(
            services => services.ConfigureDbContext<UsersDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<UserProfile>(EntityState.Modified))),
            out IServiceProvider services);
        TestSession alice = new(client);
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse failed = await alice.SendAsync(
            HttpMethod.Put,
            "api/v1/users/me",
            new { displayName = "Nunca guardado", timezone = "America/Argentina/Buenos_Aires" });
        failed.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal(0L, await services.CountAsync($"""SELECT count(*) AS "Value" FROM "UserProfiles" WHERE "AuthId" = {alice.AuthId} AND "DisplayName" = 'Nunca guardado'"""));
    }
}
