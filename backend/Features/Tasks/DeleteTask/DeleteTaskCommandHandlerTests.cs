using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Clients;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.DeleteTask;

public sealed class DeleteTaskCommandHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public DeleteTaskCommandHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_ByAnAdminWithDeletePermission_RemovesAForeignTask()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        TaskResponse task = await scope.CreateTaskAsync(HandlerCalls.Caller(TasksPermissions.Create), "Ajena");

        var deleted = await scope.Handler<DeleteTaskCommand.Handler>()
            .Handle(new DeleteTaskCommand(HandlerCalls.Caller(TasksPermissions.Delete), task.Id), TestContext.Current.CancellationToken);

        Assert.False(deleted.IsError);
        Assert.False(await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskItems
            .AnyAsync(t => t.Id == task.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_AMissingTask_IsNotFound()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();

        var deleted = await scope.Handler<DeleteTaskCommand.Handler>()
            .Handle(new DeleteTaskCommand(HandlerCalls.Caller(TasksPermissions.Delete), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal("Task.NotFound", Assert.Single(deleted.Errors).Code);
    }
}
