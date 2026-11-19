using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace TemplateApp.Tests.Support;

internal static class MonolithListener
{
    public static ValueTask StopAsync(IsolatedApi api) => Agent(api).StopAndDrainAsync();

    public static ValueTask StartAsync(IsolatedApi api) => Agent(api).StartAsync();

    private static IListeningAgent Agent(IsolatedApi api) =>
        api.Pair.Monolith.Services.GetRequiredService<IWolverineRuntime>().Endpoints
            .FindListeningAgent(new Uri($"rabbitmq://queue/{api.Prefix}-projects.task-counters"))
        ?? throw new InvalidOperationException("The monolith does not listen on the task counters queue.");
}
