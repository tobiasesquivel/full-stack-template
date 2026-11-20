using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace TemplateApp.Tests.Support;

internal sealed class FastScheduledMessages : IWolverineExtension
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    public static void Install(IServiceCollection services) => services.AddSingleton<IWolverineExtension, FastScheduledMessages>();

    public void Configure(WolverineOptions options)
    {
        options.Durability.ScheduledJobFirstExecution = TimeSpan.Zero;
        options.Durability.ScheduledJobPollingTime = Poll;
    }
}
