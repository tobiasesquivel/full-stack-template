using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Auth.RegisterUser;

public sealed class RegisterUserTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public RegisterUserTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Register_Returns201_WithSession_Then_Duplicate_Returns409_WithoutPartialRows()
    {
        TestSession alice = new(_fixture.CreateClient());
        string name = TestAccounts.UniqueName("alice");
        await alice.RegisterAsync(name);
        TestAccounts.AssertJwtIsIdentityOnly(alice.AccessToken!);

        TestSession rival = new(_fixture.CreateClient());
        await rival.CsrfAsync();
        SessionResponse duplicate = await rival.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = name },
            authenticated: false);

        duplicate.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);

        using IServiceScope scope = _fixture.Services.CreateScope();
        UserManager<AppIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
        AppIdentityUser? identity = await users.FindByNameAsync(name);
        Assert.NotNull(identity);
    }

    [Fact]
    public async Task Register_WithBlankDisplayName_Returns400_WithoutPartialRows()
    {
        TestSession bob = new(_fixture.CreateClient());
        await bob.CsrfAsync();
        string name = TestAccounts.UniqueName("bob");

        SessionResponse rejected = await bob.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = "   " },
            authenticated: false);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
        Assert.NotNull(rejected.Payload);
        using (rejected.Payload)
            Assert.True(rejected.Payload.RootElement.GetProperty("errors").TryGetProperty("DisplayName", out _));

        using (IServiceScope scope = _fixture.Services.CreateScope())
        {
            UserManager<AppIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<AppIdentityUser>>();
            Assert.Null(await users.FindByNameAsync(name));
        }

        TestSession retry = new(_fixture.CreateClient());
        await retry.RegisterAsync(name);
    }
}
