namespace TemplateApp.Infrastructure.Messaging.Outbox;

public sealed class OutboxEvents
{
    private readonly Dictionary<Type, OutboxEventType> _byClrType;
    private readonly Dictionary<(string Name, int Version), OutboxEventType> _byName;

    public OutboxEvents(IEnumerable<OutboxEventType> types)
    {
        List<OutboxEventType> all = types.ToList();
        _byClrType = all.ToDictionary(type => type.ClrType);
        _byName = all.ToDictionary(type => (type.Name, type.Version));
    }

    public OutboxEventType For(Type clrType) =>
        _byClrType.TryGetValue(clrType, out OutboxEventType? type)
            ? type
            : throw new InvalidOperationException($"{clrType.Name} has no stable name: register it with AddOutboxEvent.");

    public OutboxEventType? Find(string name, int version) =>
        _byName.GetValueOrDefault((name, version));
}
