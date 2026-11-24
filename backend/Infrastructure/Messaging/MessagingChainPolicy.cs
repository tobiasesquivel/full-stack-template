using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Logging;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class MessagingChainPolicy : IHandlerPolicy
{
    private readonly IReadOnlySet<Type> _mediatorHandlers;

    public MessagingChainPolicy(IReadOnlySet<Type> mediatorHandlers) => _mediatorHandlers = mediatorHandlers;

    public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (HandlerChain chain in chains.Where(chain => chain.HandlerCalls().Any(call => _mediatorHandlers.Contains(call.HandlerType))))
        {
            chain.FailureLogLevel = LogLevel.Debug;
            chain.OnException<Exception>().CustomAction((_, _, _) => ValueTask.CompletedTask, "mediator: rethrow without retrying");
        }
    }
}
