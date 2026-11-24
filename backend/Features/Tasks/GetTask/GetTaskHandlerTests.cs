using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.GetTask;

public sealed class GetTaskHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public GetTaskHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_AForeignTask_DependsOnTheReadPermission()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        TaskResponse task = await scope.CreateTaskAsync(HandlerCalls.Caller(TaskPermissions.Create), "Ajena");
        GetTask.Handler handler = scope.Handler<GetTask.Handler>();

        var stranger = await handler.Handle(new GetTaskCommand(HandlerCalls.Caller(), task.Id), TestContext.Current.CancellationToken);
        var admin = await handler.Handle(new GetTaskCommand(HandlerCalls.Caller(TaskPermissions.Read), task.Id), TestContext.Current.CancellationToken);

        Assert.Equal(TaskErrors.Forbidden.Code, Assert.Single(stranger.Errors).Code);
        Assert.False(admin.IsError);
        Assert.Equal((task.Id, task.Title), (admin.Value.Id, admin.Value.Title));
    }
}
