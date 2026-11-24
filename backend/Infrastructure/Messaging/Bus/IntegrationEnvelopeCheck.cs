using System.Collections.Concurrent;
using System.Globalization;
using Wolverine;

namespace TemplateApp.Infrastructure.Messaging.Bus;

public static class IntegrationEnvelopeCheck
{
    private static readonly ConcurrentDictionary<Type, IntegrationEventType> Expected = new();

    public static void Before(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Message is null || !Expected.TryGetValue(envelope.Message.GetType(), out IntegrationEventType? type))
            return;

        if (!envelope.Headers.TryGetValue(MessageHeaders.Version, out string? text)
            || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            throw new UnreadableMessageException($"{type.Name} arrived without a readable {MessageHeaders.Version} header.");
        if (version != type.Version)
            throw new UnreadableMessageException($"{type.Name} v{version} is unknown here: this saga reads v{type.Version}.");
    }

    internal static void Expect(IntegrationEventType type) => Expected[type.ClrType] = type;
}
