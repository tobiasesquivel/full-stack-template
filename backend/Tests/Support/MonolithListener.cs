using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace TemplateApp.Tests.Support;

internal static class MonolithListener
{
    public const string TaskCounters = "projects.task-counters";
    public const string Closures = "users.account-closures";

    public static ValueTask StopAsync(IsolatedApi api, string queue = TaskCounters) => Agent(api, queue).StopAndDrainAsync();

    public static ValueTask StartAsync(IsolatedApi api, string queue = TaskCounters) => Agent(api, queue).StartAsync();

    private static IListeningAgent Agent(IsolatedApi api, string queue) =>
        api.Pair.Monolith.Services.GetRequiredService<IWolverineRuntime>().Endpoints
            .FindListeningAgent(new Uri($"rabbitmq://queue/{api.Prefix}-{queue}"))
        ?? throw new InvalidOperationException($"The monolith does not listen on {queue}.");
}
