using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Messaging;

public sealed class SagaOutbox<TContext>
    where TContext : DbContext
{
    private readonly IDbContextOutbox<TContext> _outbox;

    public SagaOutbox(IDbContextOutbox<TContext> outbox)
    {
        _outbox = outbox;
    }

    public async Task StartAndCommitAsync(object saga, IEnumerable<object> messages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(messages);
        if (_outbox.DbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A saga starts inside the transaction of its first step: open it with Database.BeginTransactionAsync first.");

        _outbox.DbContext.Add(saga);
        foreach (object message in messages)
            await _outbox.PublishAsync(message);
        await _outbox.SaveChangesAndFlushMessagesAsync(ct);
    }
}
