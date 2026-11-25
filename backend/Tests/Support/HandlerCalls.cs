using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Features.Tasks;
using TemplateApp.Features.Tasks.Access;
using TemplateApp.Features.Tasks.CreateTask;
using Xunit;

namespace TemplateApp.Tests.Support;

internal static class HandlerCalls
{
    public static Caller Caller(params string[] permissions) =>
        new(Guid.NewGuid().ToString(), new HashSet<string>(permissions, StringComparer.Ordinal));

    public static THandler Handler<THandler>(this IServiceScope scope)
        where THandler : class =>
        ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);

    public static async Task<TaskResponse> CreateTaskAsync(this IServiceScope scope, Caller owner, string title)
    {
        var created = await scope.Handler<CreateTaskCommand.Handler>()
            .Handle(new CreateTaskCommand(owner, title, null), TestContext.Current.CancellationToken);
        Assert.False(created.IsError, string.Join(", ", created.ErrorsOrEmptyList.Select(error => error.Code)));
        return created.Value;
    }
}
