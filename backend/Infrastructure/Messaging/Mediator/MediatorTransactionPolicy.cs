using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Wolverine.Attributes;
using Wolverine.Configuration;
using Wolverine.Persistence;
using Wolverine.Runtime.Handlers;

namespace TemplateApp.Infrastructure.Messaging.Mediator;

internal sealed class MediatorTransactionPolicy : IChainPolicy
{
    public void Apply(IReadOnlyList<IChain> chains, GenerationRules rules, IServiceContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        Dictionary<Type, MediatorTransaction> handlers = container.Services.GetServices<MediatorHandlerType>()
            .ToDictionary(handler => handler.ClrType, handler => handler.Transaction);

        foreach (HandlerChain chain in chains.OfType<HandlerChain>())
        {
            MethodCall? call = chain.HandlerCalls().FirstOrDefault(handler => handlers.ContainsKey(handler.HandlerType));
            if (call is null)
                continue;

            switch (handlers[call.HandlerType])
            {
                case MediatorTransaction.Atomic:
                    new TransactionalAttribute { Mode = TransactionMiddlewareMode.Lightweight }.Modify(chain, rules, container);
                    chain.Middleware.Insert(0, new OpenCrossContextTransaction());
                    chain.Postprocessors.Insert(0, new StopOnBusinessError(call.Method.ReturnType.GetGenericArguments()[0]));
                    chain.Postprocessors.Add(new CompleteCrossContextTransaction());
                    break;
                case MediatorTransaction.Eager:
                    new TransactionalAttribute { Mode = TransactionMiddlewareMode.Eager }.Modify(chain, rules, container);
                    break;
                default:
                    new TransactionalAttribute { Mode = TransactionMiddlewareMode.Lightweight }.Modify(chain, rules, container);
                    break;
            }
        }
    }
}
