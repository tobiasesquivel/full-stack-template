using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Users.DeleteUser;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Features.Users.Persistence;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Users.CompleteAccountClosure;

public sealed class CompleteAccountClosureTests : IDisposable
{
    private readonly string _server;
    private readonly Uri _broker;
    private readonly List<IsolatedApi> _apis = [];

    public CompleteAccountClosureTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _server = postgres.ConnectionString;
        _broker = rabbit.Uri;
    }

    [Fact]
    public async Task APurgeThatFailsAfterDeletingTheProjects_IsDeliveredAgain_AndLeavesNothing()
    {
        FailAfterSaving<UserProfile> fault = new(EntityState.Deleted, times: 1);
        IsolatedApi api = await IsolatedApi.StartAsync(
            _server,
            _broker,
            services => services.ConfigureDbContext<UsersDbContext>((_, options) => options.AddInterceptors(fault)));
        _apis.Add(api);
        using RecordedMeasurements finished = new(api.Pair.Monolith.Services, "TemplateApp.Users", "templateapp.account_closures.finished");
        TestSession bob = await api.SignUpAsync("bob");
        await bob.CreateProjectAsync("Mudanza");
        await bob.CreateTaskAsync("Embalar");

        (_, Guid closure) = await DeleteUserTests.RequestClosureAsync(api, bob, "api/v1/users/me");
        await api.DrainAsync();

        Assert.Equal(1, fault.Failures);
        Assert.Equal(["completed"], finished.Values.Select(v => (string)v.Tags["templateapp.account_closure.outcome"]!));
        Assert.Equal("Completed", await DeleteUserBrokerTests.SagaStateAsync(api, closure));
        Assert.Equal(OwnedRows.None, await api.Pair.Monolith.Services.OwnedRowsAsync(bob.AuthId));
        Assert.Equal(1, await api.Pair.Monolith.Services.CountAsync(
            $"""SELECT count(*) AS "Value" FROM users."OutboxMessages" WHERE "Type" = 'users.user-deleted' AND "Payload"->>'authId' = {bob.AuthId}"""));
    }

    public void Dispose()
    {
        foreach (IsolatedApi api in _apis)
            api.Dispose();
    }
}
