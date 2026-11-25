using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.CreateTask;
using TemplateApp.Features.Tasks.DeleteTask;
using TemplateApp.Features.Tasks.GetTask;
using TemplateApp.Features.Tasks.ListTasks;
using TemplateApp.Features.Tasks.UpdateTask;
using TemplateApp.Hosts.Tasks;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Tests.Support;
using TemplateApp.Tests.Support.Hosts;
using Wolverine;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class MediatorContractTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public MediatorContractTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void EveryTasksCommandAndQuery_HasALocalHandler_AndEveryHandlerIsRegistered()
    {
        Type[] messages = typeof(TasksHost).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("TemplateApp.Features.Tasks.", StringComparison.Ordinal) == true)
            .Where(type => type.Name.EndsWith("Query", StringComparison.Ordinal) || (type.Name.EndsWith("Command", StringComparison.Ordinal) && !type.Name.EndsWith("IntegrationCommand", StringComparison.Ordinal)))
            .ToArray();
        Type[] handled = _fixture.TasksServices.GetServices<MediatorHandlerType>()
            .Select(handler => MessageOf(handler.ClrType))
            .ToArray();
        HandlerGraph graph = _fixture.TasksServices.GetRequiredService<HandlerGraph>();

        Assert.Equal(6, messages.Length);
        Assert.Equal(messages.OrderBy(type => type.FullName), handled.OrderBy(type => type.FullName));
        Assert.All(messages, message => Assert.NotNull(graph.HandlerFor(message)));
    }

    [Fact]
    public void EveryMediatorHandler_AnswersWithErrorOr()
    {
        IEnumerable<Type> handlers = _fixture.TasksServices.GetServices<MediatorHandlerType>().Select(handler => handler.ClrType);

        Assert.All(handlers, handler =>
        {
            Type answer = handler.GetMethod("Handle")!.ReturnType;
            Assert.Equal(typeof(Task<>), answer.GetGenericTypeDefinition());
            Assert.Equal(typeof(ErrorOr<>), answer.GetGenericArguments()[0].GetGenericTypeDefinition());
        });
    }

    [Fact]
    public async Task ACommandThatFailsValidation_ThrowsInsteadOfAnsweringAnEmptySuccess()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var owner = HandlerCalls.Caller(TaskPermissions.Create, TaskPermissions.Update);
        CancellationToken ct = TestContext.Current.CancellationToken;

        var blank = await Assert.ThrowsAsync<ValidationException>(() =>
            bus.InvokeAsync<ErrorOr<TaskResponse>>(new CreateTaskCommand(owner, "   ", null), ct));
        var unversioned = await Assert.ThrowsAsync<ValidationException>(() =>
            bus.InvokeAsync<ErrorOr<TaskResponse>>(new UpdateTaskCommand(owner, Guid.NewGuid(), "Título", false, null), ct));

        Assert.Equal(["Title"], blank.Errors.Select(error => error.PropertyName).Distinct());
        Assert.Equal(["Version"], unversioned.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public async Task ACommandThatFindsNothing_AnswersAnError()
    {
        using IServiceScope scope = _fixture.TasksServices.CreateScope();
        IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var admin = HandlerCalls.Caller(TaskPermissions.Read, TaskPermissions.Delete);
        CancellationToken ct = TestContext.Current.CancellationToken;

        ErrorOr<TaskResponse> read = await bus.InvokeAsync<ErrorOr<TaskResponse>>(new GetTaskQuery(admin, Guid.NewGuid()), ct);
        ErrorOr<Success> deleted = await bus.InvokeAsync<ErrorOr<Success>>(new DeleteTaskCommand(admin, Guid.NewGuid()), ct);
        ErrorOr<TaskListResponse> listed = await bus.InvokeAsync<ErrorOr<TaskListResponse>>(new ListTasksQuery(admin, "not-a-cursor", 20), ct);

        Assert.Equal("Task.NotFound", Assert.Single(read.Errors).Code);
        Assert.Equal("Task.NotFound", Assert.Single(deleted.Errors).Code);
        Assert.True(listed.IsError);
    }

    private static Type MessageOf(Type handler) => handler.GetMethod("Handle")!.GetParameters()[0].ParameterType;
}
