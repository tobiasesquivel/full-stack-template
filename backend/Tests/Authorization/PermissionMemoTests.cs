using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Tests.Authorization;

public sealed class PermissionMemoTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public PermissionMemoTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeveralChecksInOneRequest_ReadIdentityOnce()
    {
        IdentityCommandCounter counter = new();
        TestSession alice = new(_fixture.CreateIsolatedClient(services => AddProbe(services, counter), out _));
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        counter.Reset();
        Assert.Equal(new[] { true }, await ProbeAsync(alice, alice.AuthId, checks: 1));
        int oneCheck = counter.Touching("AspNet");
        Assert.True(oneCheck > 0, "A permission check must read Identity.");

        counter.Reset();
        Assert.Equal(new[] { true, false, true, false }, await ProbeAsync(alice, alice.AuthId, checks: 4));
        Assert.Equal(oneCheck, counter.Touching("AspNet"));
    }

    [Fact]
    public async Task OpenSession_ReadsRolesOnce()
    {
        IdentityCommandCounter counter = new();
        TestSession alice = new(_fixture.CreateIsolatedClient(services => AddProbe(services, counter), out _));
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        counter.Reset();
        SessionResponse opened = await alice.OpenAsync();
        opened.Payload?.Dispose();
        Assert.Equal(HttpStatusCode.OK, opened.Status);

        Assert.Equal(1, counter.Touching("AspNetUserRoles"));
    }

    [Fact]
    public async Task PermissionChange_StaysOutOfTheCurrentScope_AndAppliesOnTheNextRequest()
    {
        TestSession bob = new(_fixture.CreateIsolatedClient(services => AddProbe(services, new IdentityCommandCounter()), out IServiceProvider services));
        string bobName = TestAccounts.UniqueName("bob");
        await bob.RegisterAsync(bobName);

        Assert.Equal(new[] { true, false }, await ProbeAsync(bob, bob.AuthId, checks: 2));

        using (IServiceScope request = services.CreateScope())
        {
            IAuthModule auth = request.ServiceProvider.GetRequiredService<IAuthModule>();
            Assert.False(await auth.HasPermissionAsync(bob.AuthId, TaskPermissions.Read, CancellationToken.None));

            await _fixture.SetRoleAsync(bobName, "Admin", member: true);

            Assert.False(await auth.HasPermissionAsync(bob.AuthId, TaskPermissions.Read, CancellationToken.None));
        }

        Assert.Equal(new[] { true, true }, await ProbeAsync(bob, bob.AuthId, checks: 2));

        await _fixture.SetRoleAsync(bobName, "Admin", member: false);

        Assert.Equal(new[] { true, false }, await ProbeAsync(bob, bob.AuthId, checks: 2));
    }

    private static void AddProbe(IServiceCollection services, IdentityCommandCounter counter)
    {
        services.AddSingleton<IStartupFilter, PermissionProbe>();
        services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(counter));
    }

    private static async Task<bool[]> ProbeAsync(TestSession session, string userId, int checks)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, PermissionProbe.Route(userId, checks), csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
            return response.Payload.RootElement.EnumerateArray().Select(result => result.GetBoolean()).ToArray();
    }
}
