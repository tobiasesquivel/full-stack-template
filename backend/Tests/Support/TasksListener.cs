using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace TemplateApp.Tests.Support;

internal static class TasksListener
{
    public static ValueTask StopAsync(IsolatedApi api) => Agent(api).StopAndDrainAsync();

    public static ValueTask StartAsync(IsolatedApi api) => Agent(api).StartAsync();

    private static IListeningAgent Agent(IsolatedApi api) =>
        api.Pair.Tasks.Services.GetRequiredService<IWolverineRuntime>().Endpoints
            .FindListeningAgent(new Uri($"rabbitmq://queue/{api.Prefix}-tasks.user-deletions"))
        ?? throw new InvalidOperationException("Tasks does not listen on the user deletions queue.");
}
