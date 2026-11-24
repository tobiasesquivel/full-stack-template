using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.UpdateTask;

public sealed class UpdateTaskHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public UpdateTaskHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_ByTheOwner_CompletesTheTask()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        var owner = HandlerCalls.Caller(TaskPermissions.Create);
        TaskResponse task = await scope.CreateTaskAsync(owner, "Antes");

        var updated = await scope.Handler<UpdateTask.Handler>()
            .Handle(new UpdateTaskCommand(owner, task.Id, "Después", true, task.Version), TestContext.Current.CancellationToken);

        Assert.False(updated.IsError);
        Assert.Equal(("Después", true), (updated.Value.Title, updated.Value.IsCompleted));
    }

    [Fact]
    public async Task Handle_ByAStrangerWithoutPermission_IsForbidden()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        TaskResponse task = await scope.CreateTaskAsync(HandlerCalls.Caller(TaskPermissions.Create), "Ajena");

        var updated = await scope.Handler<UpdateTask.Handler>()
            .Handle(new UpdateTaskCommand(HandlerCalls.Caller(), task.Id, "Robada", false, task.Version), TestContext.Current.CancellationToken);

        Assert.Equal(TaskErrors.Forbidden.Code, Assert.Single(updated.Errors).Code);
    }
}
