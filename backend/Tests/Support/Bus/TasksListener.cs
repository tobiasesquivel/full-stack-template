using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Tests.Support.Hosts;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace TemplateApp.Tests.Support.Bus;

internal static class TasksListener
{
    private static readonly string[] Queues = ["tasks.user-deletions", "tasks.account-closures"];

    public static async Task StopAsync(IsolatedApi api)
    {
        foreach (IListeningAgent agent in Agents(api))
            await agent.StopAndDrainAsync();
    }

    public static async Task StartAsync(IsolatedApi api)
    {
        foreach (IListeningAgent agent in Agents(api))
            await agent.StartAsync();
    }

    private static IEnumerable<IListeningAgent> Agents(IsolatedApi api) =>
        Queues.Select(queue => api.Pair.Tasks.Services.GetRequiredService<IWolverineRuntime>().Endpoints
            .FindListeningAgent(new Uri($"rabbitmq://queue/{api.Prefix}-{queue}"))
            ?? throw new InvalidOperationException($"Tasks does not listen on {queue}."));
}
