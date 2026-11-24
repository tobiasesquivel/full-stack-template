using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Tests.Support;
using Xunit;

namespace TemplateApp.Features.Tasks.ListTasks;

public sealed class ListTasksHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ListTasksHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_ListsOnlyTheCallersTasks_NewestFirst()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        var owner = HandlerCalls.Caller(TaskPermissions.Create);
        await scope.CreateTaskAsync(owner, "Primera");
        await scope.CreateTaskAsync(owner, "Segunda");
        await scope.CreateTaskAsync(HandlerCalls.Caller(TaskPermissions.Create), "De otro");

        var listed = await scope.Handler<ListTasks.Handler>()
            .Handle(new ListTasksCommand(owner, null, 20), TestContext.Current.CancellationToken);

        Assert.False(listed.IsError);
        Assert.Equal(["Segunda", "Primera"], listed.Value.Items.Select(item => item.Title));
        Assert.Null(listed.Value.NextCursor);
    }

    [Fact]
    public async Task Handle_AnInvalidPageSize_IsRejected()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();

        var listed = await scope.Handler<ListTasks.Handler>()
            .Handle(new ListTasksCommand(HandlerCalls.Caller(), null, 0), TestContext.Current.CancellationToken);

        Assert.True(listed.IsError);
    }
}
