using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace TemplateApp.Infrastructure.Messaging.Sagas;

public sealed class SagaOutbox<TContext>
    where TContext : DbContext
{
    private readonly TContext _db;
    private readonly IMessageBus _bus;

    public SagaOutbox(TContext db, IMessageBus bus)
    {
        _db = db;
        _bus = bus;
    }

    public async Task StartAsync(object saga, IEnumerable<object> messages)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(messages);
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A saga starts inside the Eager transaction that Wolverine opens around the handler of its first step.");

        _db.Add(saga);
        foreach (object message in messages)
            await _bus.PublishAsync(message);
    }
}
