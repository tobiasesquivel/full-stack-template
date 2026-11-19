using System.Collections.Concurrent;
using Wolverine.Util;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class IntegrationEventNaming : IMessageTypeNaming
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();

    public static void Register(IntegrationEventType type)
    {
        string name = Names.GetOrAdd(type.ClrType, type.Name);
        if (name != type.Name)
            throw new InvalidOperationException($"{type.ClrType.Name} is already named {name} on the wire, not {type.Name}.");

        WolverineMessageNaming.InsertFirst<IntegrationEventNaming>();
    }

    public bool TryDetermineName(Type messageType, out string messageTypeName) =>
        Names.TryGetValue(messageType, out messageTypeName!);
}
