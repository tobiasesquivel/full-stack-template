using System.Net;
using System.Text.Json;
using Npgsql;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Features.Users.ScheduleAccountClosure;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.GetAccountClosureHistory;

public sealed class GetAccountClosureHistoryTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public GetAccountClosureHistoryTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task TheOwnerAndAUserReaderReadTheHistoryInOrder_NobodyElseDoes_AndTheDatabaseRefusesToRewriteIt()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker);
        _apis.Add(api);
        TestSession bob = await api.SignUpAsync("bob");
        TestSession eve = await api.SignUpAsync("eve");
        TestSession admin = await api.SignUpAsync("admin");
        await api.Pair.Monolith.Services.SetRoleAsync(admin.UserName, "SuperAdmin", member: true);
        (_, Guid closure, DateTimeOffset closesAt) = await DeleteUserTests.ScheduleClosureAsync(bob, "api/v1/users/me");
        SessionResponse withdrawn = await AccountClosureGracePeriodTests.CancelAsync(admin, closure);
        withdrawn.Payload?.Dispose();

        JsonElement seenByBob = await HistoryAsync(bob, closure);
        JsonElement seenByAdmin = await HistoryAsync(admin, closure);
        SessionResponse seenByEve = await eve.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{closure}/history", csrf: false);
        SessionResponse unknown = await admin.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{Guid.NewGuid()}/history", csrf: false);
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(api.Database);
        PostgresException update = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, """UPDATE users."AccountClosureHistory" SET "Actor" = 'someone-else' WHERE "ClosureId" = $1""", closure));
        PostgresException delete = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, """DELETE FROM users."AccountClosureHistory" WHERE "ClosureId" = $1""", closure));

        JsonElement[] items = [.. seenByBob.GetProperty("items").EnumerateArray()];
        Assert.Equal(closure, seenByBob.GetProperty("closureId").GetGuid());
        Assert.Equal(
            [("requested", bob.AuthId, closesAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), ("cancelled", admin.AuthId, null)],
            items.Select(i => (i.GetProperty("kind").GetString()!, i.GetProperty("actor").GetString()!, i.GetProperty("detail").GetString())));
        Assert.True(items[0].GetProperty("occurredAt").GetDateTimeOffset() <= items[1].GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(seenByBob.ToString(), seenByAdmin.ToString());
        ProblemAssert.Problem(seenByEve, HttpStatusCode.Forbidden, "User.Forbidden");
        ProblemAssert.Problem(unknown, HttpStatusCode.NotFound, "User.ClosureNotFound");
        Assert.Equal((PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.RestrictViolation), (update.SqlState, delete.SqlState));
        Assert.Equal(["requested", "cancelled"], await AccountClosureGracePeriodTests.HistoryAsync(api, closure));
    }

    [Fact]
    public async Task TheHistorySurvivesTheAccount_AndEndsWithTheResultOfTheClosure()
    {
        IsolatedApi api = await IsolatedApi.StartAsync(_server, _broker);
        _apis.Add(api);
        TestSession bob = await api.SignUpAsync("bob");
        TestSession admin = await api.SignUpAsync("admin");
        await api.Pair.Monolith.Services.SetRoleAsync(admin.UserName, "SuperAdmin", member: true);

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        await api.DrainAsync();
        JsonElement afterTheAccount = await HistoryAsync(admin, closure);

        Assert.Equal(
            ["requested", "grace-expired", "closure-started", "completed"],
            afterTheAccount.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()));
        Assert.All(afterTheAccount.GetProperty("items").EnumerateArray().Skip(1), i => Assert.Equal("system", i.GetProperty("actor").GetString()));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }

    private static async Task<JsonElement> HistoryAsync(TestSession session, Guid closureId)
    {
        SessionResponse response = await session.SendAsync(HttpMethod.Get, $"api/v1/users/closures/{closureId}/history", csrf: false);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.Payload);
        using (response.Payload)
            return response.Payload.RootElement.Clone();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, Guid closureId)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = closureId });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
