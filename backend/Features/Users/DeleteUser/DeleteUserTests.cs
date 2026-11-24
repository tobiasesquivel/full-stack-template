using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Features.Users.ScheduleAccountClosure;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Faults;
using TemplateApp.Tests.Support.Hosts;
using TemplateApp.Tests.Support.Rows;
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
    public async Task DeleteMe_SchedulesTheClosure_AndASecondDeleteDuringTheGracePeriodAnswersTheSameClosure()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        (SessionResponse first, Guid closure, DateTimeOffset closesAt) = await ScheduleClosureAsync(alice, "api/v1/users/me");
        (_, Guid again, DateTimeOffset againClosesAt) = await ScheduleClosureAsync(alice, "api/v1/users/me");

        Assert.Equal("/api/v1/users/me", first.Location);
        Assert.Equal((closure, closesAt), (again, againClosesAt));
        JsonElement me = await MeAsync(alice, "api/v1/users/me");
        Assert.Equal("scheduled", me.GetProperty("accountStatus").GetString());
        Assert.Equal(closure, me.GetProperty("closureId").GetGuid());
        Assert.Equal(closesAt, me.GetProperty("closesAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("lastClosure").ValueKind);
        Assert.InRange(closesAt - await RequestedAtAsync(_fixture.Services, closure), TimeSpan.FromDays(14) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(14));
        Assert.Equal(1, await GracePeriodsAsync(_fixture.Services, alice.AuthId));
        Assert.Equal(1, await RequestedFactsAsync(_fixture.Services, alice.AuthId));
        Assert.Equal(0, await ClosureRequestsAsync(_fixture.Services, alice.AuthId));
        Assert.Equal(1, (await _fixture.Services.OwnedRowsAsync(alice.AuthId)).Accounts);
        await alice.CreateTaskAsync("Durante el período de gracia");
    }

    [Fact]
    public async Task ASuperAdminClosingAnotherAccount_GetsTheLocationOfThatUser_AndIsRecordedAsWhoAsked()
    {
        TestSession alice = new(_fixture.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));
        TestSession admin = new(_fixture.CreateClient());
        string adminName = TestAccounts.UniqueName("admin");
        await admin.RegisterAsync(adminName);
        await _fixture.SetRoleAsync(adminName, "SuperAdmin", member: true);

        (SessionResponse accepted, Guid closure, _) = await ScheduleClosureAsync(admin, $"api/v1/users/{alice.AuthId}");

        Assert.Equal($"/api/v1/users/{alice.AuthId}", accepted.Location);
        JsonElement seenByAdmin = await MeAsync(admin, $"api/v1/users/{alice.AuthId}");
        JsonElement seenByAlice = await MeAsync(alice, "api/v1/users/me");
        Assert.Equal("scheduled", seenByAdmin.GetProperty("accountStatus").GetString());
        Assert.Equal(seenByAlice.GetProperty("accountStatus").GetString(), seenByAdmin.GetProperty("accountStatus").GetString());
        Assert.Equal(closure, seenByAlice.GetProperty("closureId").GetGuid());
        Assert.Equal(1, await GracePeriodsAsync(_fixture.Services, alice.AuthId));
        Assert.Equal(admin.AuthId, await _fixture.Services.TextAsync(
            $"""SELECT "Actor" AS "Value" FROM users."AccountClosureHistory" WHERE "ClosureId" = {closure} AND "Kind" = 'requested'"""));
    }

    [Fact]
    public async Task WhenSavingTheClosureFails_TheProfileStaysActive_AndNothingIsScheduled()
    {
        ApiPair api = _fixture.CreateIsolatedApi(
            new Dictionary<string, string>(),
            services => services.ConfigureDbContext<UsersDbContext>((_, options) => options.AddInterceptors(new FailAfterSaving<UserProfile>(EntityState.Modified))));
        TestSession alice = new(api.CreateClient());
        await alice.RegisterAsync(TestAccounts.UniqueName("alice"));

        SessionResponse failed = await alice.SendAsync(HttpMethod.Delete, "api/v1/users/me");

        ProblemAssert.Problem(failed, HttpStatusCode.InternalServerError, "Http.Unexpected");
        Assert.Equal("active", (await MeAsync(alice, "api/v1/users/me")).GetProperty("accountStatus").GetString());
        Assert.Equal(0, await GracePeriodsAsync(api.Monolith.Services, alice.AuthId));
        Assert.Equal(0, await RequestedFactsAsync(api.Monolith.Services, alice.AuthId));
    }

    internal static async Task<(SessionResponse Response, Guid ClosureId, DateTimeOffset ClosesAt)> ScheduleClosureAsync(TestSession session, string route)
    {
        SessionResponse accepted = await session.SendAsync(HttpMethod.Delete, route);
        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        Assert.NotNull(accepted.Payload);
        using (accepted.Payload)
        {
            JsonElement body = accepted.Payload.RootElement;
            Assert.Equal("scheduled", body.GetProperty("status").GetString());
            return (accepted, body.GetProperty("closureId").GetGuid(), body.GetProperty("closesAt").GetDateTimeOffset());
        }
    }

    internal static async Task<(SessionResponse Response, Guid ClosureId)> RequestClosureAsync(IsolatedApi api, TestSession session, string route)
    {
        (SessionResponse accepted, Guid closureId, _) = await ScheduleClosureAsync(session, route);
        await AccountClosureGracePeriodTests.EndGracePeriodAsync(api, closureId);
        return (accepted, closureId);
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

    private static Task<long> GracePeriodsAsync(IServiceProvider services, string authId) =>
        services.CountAsync($"""SELECT count(*) AS "Value" FROM users."AccountClosureGracePeriods" WHERE "UserId" = {authId}""");

    private static Task<long> RequestedFactsAsync(IServiceProvider services, string authId) =>
        services.CountAsync($"""SELECT count(*) AS "Value" FROM users."AccountClosureHistory" WHERE "UserId" = {authId} AND "Kind" = 'requested'""");

    private static async Task<DateTimeOffset> RequestedAtAsync(IServiceProvider services, Guid closureId)
    {
        using IServiceScope scope = services.CreateScope();
        UsersDbContext db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        return await db.AccountClosureHistory
            .Where(e => e.ClosureId == closureId && e.Kind == AccountClosureFacts.Requested)
            .Select(e => e.OccurredAt)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
