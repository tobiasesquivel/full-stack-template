using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Logging;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class MessagingChainPolicy : IHandlerPolicy
{
    private readonly IReadOnlySet<Type> _sagas;
    private readonly IReadOnlySet<Type> _mediatorHandlers;

    public MessagingChainPolicy(IReadOnlySet<Type> sagas, IReadOnlySet<Type> mediatorHandlers)
    {
        _sagas = sagas;
        _mediatorHandlers = mediatorHandlers;
    }

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (HandlerChain chain in chains)
        {
            IEnumerable<Type> handlers = chain.HandlerCalls().Select(call => call.HandlerType);
            if (handlers.Any(_mediatorHandlers.Contains))
                chain.FailureLogLevel = LogLevel.Debug;
            else if (handlers.Any(_sagas.Contains))
                QueueFailureRules.Apply(chain);
        }
    }
}
