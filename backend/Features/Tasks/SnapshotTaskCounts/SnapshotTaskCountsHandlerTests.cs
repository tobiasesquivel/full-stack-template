using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.SnapshotTaskCounts;

public sealed class SnapshotTaskCountsHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public SnapshotTaskCountsHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_CountsTheUndeliveredEventOfANewTask()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        await scope.CreateTaskAsync(HandlerCalls.Caller(TaskPermissions.Create), "Recién creada");

        var snapshot = await scope.Handler<SnapshotTaskCounts.Handler>()
            .Handle(new SnapshotTaskCountsCommand(), TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsError);
        Assert.NotEmpty(snapshot.Value.UndeliveredMessageIds);
    }
}
