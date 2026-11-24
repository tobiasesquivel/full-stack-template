using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TemplateApp.Infrastructure.Messaging.Outbox;

namespace TemplateApp.Tests.Messaging;

internal sealed class FailOnceMarkingProcessed : SaveChangesInterceptor
{
    private int _failed;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        bool marksProcessed = eventData.Context is not null
            && eventData.Context.ChangeTracker.Entries<OutboxMessage>()
                .Any(e => e.State == EntityState.Modified && e.Entity.ProcessedAt is not null);
        if (marksProcessed && Interlocked.Exchange(ref _failed, 1) == 0)
            throw new InvalidOperationException("The relay stopped after the consumer ran and before it marked the message.");

        return ValueTask.FromResult(result);
    }
}
