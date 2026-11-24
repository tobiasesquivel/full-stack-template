using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Wolverine.Attributes;
using Wolverine.Configuration;
using Wolverine.Persistence;
using Wolverine.Persistence.Sagas;
using Wolverine.Runtime.Handlers;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class MediatorTransactionPolicy : IChainPolicy
{
    private readonly Dictionary<Type, MediatorTransaction> _handlers;

    public MediatorTransactionPolicy(IEnumerable<MediatorHandlerType> handlers) =>
        _handlers = handlers.ToDictionary(handler => handler.ClrType, handler => handler.Transaction);

    public void Apply(IReadOnlyList<IChain> chains, GenerationRules rules, IServiceContainer container)
    {
        if (rules.PersistenceProviders().Count == 0)
            return;

        foreach (HandlerChain chain in chains.OfType<HandlerChain>())
        {
            MethodCall? call = chain.HandlerCalls().FirstOrDefault(handler => _handlers.ContainsKey(handler.HandlerType));
            if (call is null)
                continue;

            switch (_handlers[call.HandlerType])
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
