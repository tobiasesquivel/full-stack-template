using System.Net;
using System.Text.Json;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Features.Users.OrchestrateAccountClosure;
using TemplateApp.Features.Users.ScheduleAccountClosure;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Users.GetAccountClosure;

public sealed class GetAccountClosureTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public GetAccountClosureTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task TheOwnerAndASuperAdminSeeTheClosure_EvenAfterTheAccountIsGone_AndNobodyElseDoes()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker);
        _apis.Add(api);
        TestSession bob = await api.SignUpAsync("bob");
        TestSession eve = await api.SignUpAsync("eve");
        TestSession admin = await api.SignUpAsync("admin");
        await api.Pair.Monolith.Services.SetRoleAsync(admin.UserName, "SuperAdmin", member: true);

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        JsonElement seenByBob = await AccountClosureSagaTests.ClosureAsync(bob, closure);
        SessionResponse seenByEve = await eve.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{closure}", csrf: false);
        SessionResponse unknown = await admin.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{Guid.NewGuid()}", csrf: false);
        await api.DrainAsync();
        JsonElement seenByAdmin = await AccountClosureSagaTests.ClosureAsync(admin, closure);
        SessionResponse bobAfter = await bob.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{closure}", csrf: false);
        bobAfter.Payload?.Dispose();

        Assert.Equal((closure, bob.AuthId), (seenByBob.GetProperty("closureId").GetGuid(), seenByBob.GetProperty("userId").GetString()));
        Assert.Equal(JsonValueKind.Null, seenByBob.GetProperty("finishedAt").ValueKind);
        Assert.False(seenByBob.GetProperty("overdue").GetBoolean());
        ProblemAssert.Problem(seenByEve, HttpStatusCode.Forbidden, "User.Forbidden");
        ProblemAssert.Problem(unknown, HttpStatusCode.NotFound, "User.ClosureNotFound");
        Assert.Equal("completed", seenByAdmin.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, seenByAdmin.GetProperty("closesAt").ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, bobAfter.Status);
    }

    [Fact]
    public async Task AScheduledClosure_ReadsAsScheduledWithItsDate_AndAsCancelledOnceWithdrawn()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker);
        _apis.Add(api);
        TestSession bob = await api.SignUpAsync("bob");
        TestSession eve = await api.SignUpAsync("eve");

        (_, Guid closure, DateTimeOffset closesAt) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        JsonElement scheduled = await AccountClosureSagaTests.ClosureAsync(bob, closure);
        SessionResponse seenByEve = await eve.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{closure}", csrf: false);
        SessionResponse withdrawn = await AccountClosureGracePeriodTests.CancelAsync(bob, closure);
        withdrawn.Payload?.Dispose();
        JsonElement cancelled = await AccountClosureSagaTests.ClosureAsync(bob, closure);

        Assert.Equal(("scheduled", bob.AuthId), (scheduled.GetProperty("state").GetString(), scheduled.GetProperty("userId").GetString()));
        Assert.Equal(closesAt, scheduled.GetProperty("closesAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, scheduled.GetProperty("finishedAt").ValueKind);
        ProblemAssert.Problem(seenByEve, HttpStatusCode.Forbidden, "User.Forbidden");
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.Status);
        Assert.Equal("cancelled", cancelled.GetProperty("state").GetString());
        Assert.Equal(closesAt, cancelled.GetProperty("closesAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.String, cancelled.GetProperty("finishedAt").ValueKind);
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }
}
