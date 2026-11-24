using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports.Local;

namespace TemplateApp.Infrastructure.Messaging.Mediator;

internal sealed class DurableLocalQueuesPolicy : IEndpointPolicy
{
    public void Apply(Wolverine.Configuration.Endpoint endpoint, IWolverineRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(runtime);
        if (endpoint is not LocalQueue)
            return;

        HashSet<string> mediatorQueues = runtime.Services.GetServices<MediatorHandlerType>()
            .Select(handler => handler.ClrType.GetMethod("Handle")!.GetParameters()[0].ParameterType.FullName!.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (!mediatorQueues.Contains(endpoint.EndpointName))
            endpoint.Mode = EndpointMode.Durable;
    }
}
