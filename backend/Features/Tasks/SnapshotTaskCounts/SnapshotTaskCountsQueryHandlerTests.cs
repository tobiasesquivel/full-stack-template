using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

public sealed class SnapshotTaskCountsQueryHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public SnapshotTaskCountsQueryHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_CountsTheUndeliveredEventOfANewTask()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        await scope.CreateTaskAsync(HandlerCalls.Caller(TasksPermissions.Create), "Recién creada");

        var snapshot = await scope.Handler<SnapshotTaskCountsQuery.Handler>()
            .Handle(new SnapshotTaskCountsQuery(), TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsError);
        Assert.NotEmpty(snapshot.Value.UndeliveredMessageIds);
    }
}
