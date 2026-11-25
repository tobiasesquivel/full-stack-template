using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.ListTasks;

public sealed class ListTasksQueryHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public ListTasksQueryHandlerTests(ApiFixture fixture)
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

        var listed = await scope.Handler<ListTasksQuery.Handler>()
            .Handle(new ListTasksQuery(owner, null, 20), TestContext.Current.CancellationToken);

        Assert.False(listed.IsError);
        Assert.Equal(["Segunda", "Primera"], listed.Value.Items.Select(item => item.Title));
        Assert.Null(listed.Value.NextCursor);
    }
}
