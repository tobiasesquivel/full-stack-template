using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Auth.Persistence;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
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
        SessionResponse registered = await alice.RegisterAsync(name);
        Assert.Equal($"/api/v1/users/{alice.AuthId}", registered.Location);

        TestSession rival = new(_fixture.CreateClient());
        await rival.CsrfAsync();
        SessionResponse duplicate = await rival.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = name });

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
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = "   " });

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

    [Fact]
    public async Task Register_WhenItFailsAfterTheProfileWasSaved_LeavesNoAccountAndNoProfile()
    {
        HttpClient client = _fixture.CreateIsolatedClient(
            services => services.ConfigureDbContext<UsersDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<UserProfile>())),
            out IServiceProvider services);
        TestSession carol = new(client);
        await carol.CsrfAsync();
        string name = TestAccounts.UniqueName("carol");

        SessionResponse failed = await carol.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = name });
        failed.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal(0L, await services.CountAsync($"""SELECT count(*) AS "Value" FROM "AspNetUsers" WHERE "UserName" = {name}"""));
        Assert.Equal(0L, await services.CountAsync($"""SELECT count(*) AS "Value" FROM "UserProfiles" WHERE "DisplayName" = {name}"""));
    }

    [Fact]
    public async Task Register_WhenItFailsAfterTheSessionWasSaved_LeavesNeitherAccountNorProfile()
    {
        HttpClient client = _fixture.CreateIsolatedClient(
            services => services.ConfigureDbContext<AuthDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<Session>())),
            out IServiceProvider services);
        TestSession dave = new(client);
        await dave.CsrfAsync();
        string name = TestAccounts.UniqueName("dave");

        SessionResponse failed = await dave.SendAsync(
            HttpMethod.Post,
            "api/v1/auth/register",
            new { userName = name, email = $"{name}@example.test", password = $"Smoke!{Guid.NewGuid():N}Aa9", displayName = name });
        failed.Payload?.Dispose();

        Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
        Assert.Equal(0L, await services.CountAsync($"""SELECT count(*) AS "Value" FROM "AspNetUsers" WHERE "UserName" = {name}"""));
        Assert.Equal(0L, await services.CountAsync($"""SELECT count(*) AS "Value" FROM "UserProfiles" WHERE "DisplayName" = {name}"""));
    }
}
