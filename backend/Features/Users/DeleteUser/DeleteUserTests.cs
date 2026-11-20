using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
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
    public async Task DeleteMe_Answers202WithTheClosure_AndASecondDeleteAnswersTheSameClosure()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        (SessionResponse first, Guid closure) = await RequestClosureAsync(alice, "api/v1/users/me");
        (_, Guid again) = await RequestClosureAsync(alice, "api/v1/users/me");

        Assert.Equal("/api/v1/users/me", first.Location);
        Assert.Equal(closure, again);
        JsonElement me = await MeAsync(alice, "api/v1/users/me");
        Assert.Equal("closing", me.GetProperty("accountStatus").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("lastClosure").ValueKind);
        Assert.Equal(1, await ClosureRequestsAsync(_fixture.Services, alice.AuthId));
        Assert.Equal(1, (await _fixture.Services.OwnedRowsAsync(alice.AuthId)).Accounts);
    }

    [Fact]
    public async Task ASuperAdminClosingAnotherAccount_GetsTheLocationOfThatUser_WithTheSameClosureFields()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession admin = new(_fixture.CreateClient());
        string adminName = TestAccounts.UniqueName("admin");
        await admin.RegisterAsync(adminName);
        await _fixture.SetRoleAsync(adminName, "SuperAdmin", member: true);

        (SessionResponse accepted, Guid closure) = await RequestClosureAsync(admin, $"api/v1/users/{alice.AuthId}");

        Assert.Equal($"/api/v1/users/{alice.AuthId}", accepted.Location);
        JsonElement seenByAdmin = await MeAsync(admin, $"api/v1/users/{alice.AuthId}");
        JsonElement seenByAlice = await MeAsync(alice, "api/v1/users/me");
        Assert.Equal("closing", seenByAdmin.GetProperty("accountStatus").GetString());
        Assert.Equal(seenByAlice.GetProperty("accountStatus").GetString(), seenByAdmin.GetProperty("accountStatus").GetString());
        Assert.Equal(seenByAlice.GetProperty("lastClosure").ValueKind, seenByAdmin.GetProperty("lastClosure").ValueKind);
        Assert.Equal(1, await ClosureRequestsAsync(_fixture.Services, alice.AuthId));
        Assert.NotEqual(Guid.Empty, closure);
    }

    [Fact]
    public async Task WhenSavingTheClosureFails_TheProfileStaysActive_AndNoEventLeaves()
    {
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string>(),
            services => services.ConfigureDbContext<UsersDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<UserProfile>(EntityState.Modified))));
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse failed = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");

        ProblemAssert.Problem(failed, HttpStatusCode.InternalServerError, "Http.Unexpected");
        Assert.Equal("active", (await MeAsync(alice, "api/v1/users/me")).GetProperty("accountStatus").GetString());
        Assert.Equal(0, await ClosureRequestsAsync(api.Monolith.Services, alice.AuthId));
    }

    internal static async Task<(SessionResponse Response, Guid ClosureId)> RequestClosureAsync(TestSession session, string route)
    {
        SessionResponse accepted = await session.SendAsync(HttpMethod.Delete, route);
        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        Assert.NotNull(accepted.Payload);
        using (accepted.Payload)
        {
            Assert.Equal("closing", accepted.Payload.RootElement.GetProperty("status").GetString());
            return (accepted, accepted.Payload.RootElement.GetProperty("closureId").GetGuid());
        }
    }

    internal static async Task<JsonElement> MeAsync(TestSession session, string route)
    {
        SessionResponse me = await session.SendAsync(HttpMethod.Get, route, csrf: false);
        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.NotNull(me.Payload);
        using (me.Payload)
            return me.Payload.RootElement.Clone();
    }

    private static Task<long> ClosureRequestsAsync(IServiceProvider services, string authId) =>
        services.CountAsync($"""SELECT count(*) AS "Value" FROM users."AccountClosureSagas" WHERE "UserId" = {authId}""");
}
