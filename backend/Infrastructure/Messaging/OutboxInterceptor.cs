using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging;

public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private readonly OutboxEvents _events;
    private readonly TimeProvider _clock;

    public OutboxInterceptor(OutboxEvents events, TimeProvider clock)
    {
        _events = events;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
            Enqueue(eventData.Context);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
            Enqueue(eventData.Context);

        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
            Forget(eventData.Context);

        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
            Forget(eventData.Context);

        return ValueTask.FromResult(result);
    }

    private void Enqueue(DbContext context)
    {
        foreach (EntityEntry<OutboxMessage> unsaved in context.ChangeTracker.Entries<OutboxMessage>().Where(e => e.State == EntityState.Added).ToList())
            unsaved.State = EntityState.Detached;

        List<IDomainEvent> events = context.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToList();
        if (events.Count == 0)
            return;

        DateTimeOffset now = _clock.GetUtcNow();
        string? traceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
        context.AddRange(events.Select(@event => Message(@event, now, traceParent)).ToList());
    }

    private OutboxMessage Message(IDomainEvent @event, DateTimeOffset now, string? traceParent)
    {
        OutboxEventType type = _events.For(@event.GetType());
        return new OutboxMessage
        {
            MessageId = Guid.CreateVersion7(now),
            Type = type.Name,
            Version = type.Version,
            Payload = JsonSerializer.Serialize(@event, type.ClrType, JsonSerializerOptions.Web),
            OccurredAt = now,
            TraceParent = traceParent,
        };
    }

    private static void Forget(DbContext context)
    {
        foreach (EntityEntry<IHasDomainEvents> entry in context.ChangeTracker.Entries<IHasDomainEvents>().ToList())
            entry.Entity.ClearDomainEvents();
    }
}
