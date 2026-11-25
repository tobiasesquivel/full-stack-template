using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Features.Tasks.CreateTask;

public sealed class CreateTaskCommandHandlerTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CreateTaskCommandHandlerTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_WithCreatePermission_StoresTheTaskForTheCaller()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        var owner = HandlerCalls.Caller(TaskPermissions.Create);

        var created = await scope.Handler<CreateTaskCommand.Handler>()
            .Handle(new CreateTaskCommand(owner, "  Sin HTTP  ", null), TestContext.Current.CancellationToken);

        Assert.False(created.IsError);
        Assert.Equal("Sin HTTP", created.Value.Title);
        string storedOwner = await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskItems
            .Where(t => t.Id == created.Value.Id)
            .Select(t => t.OwnerId)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(owner.Id, storedOwner);
    }

    [Fact]
    public async Task Handle_WithoutCreatePermission_IsForbidden()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();

        var created = await scope.Handler<CreateTaskCommand.Handler>()
            .Handle(new CreateTaskCommand(HandlerCalls.Caller(), "Sin permiso", null), TestContext.Current.CancellationToken);

        Assert.Equal(TaskErrors.Forbidden.Code, Assert.Single(created.Errors).Code);
    }
}
