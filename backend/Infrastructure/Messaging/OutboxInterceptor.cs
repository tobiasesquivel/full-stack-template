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
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<EventHandlerRegistration> _handlers;

    public OutboxInterceptor(OutboxEvents events, TimeProvider clock, IServiceProvider services, IEnumerable<EventHandlerRegistration> handlers)
    {
        _events = events;
        _clock = clock;
        _services = services;
        _handlers = handlers.ToList();
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

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not null)
        {
            await PublishAsync(eventData.Context, cancellationToken);
            Forget(eventData.Context);
        }

        return result;
    }

    private async Task PublishAsync(DbContext context, CancellationToken ct)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        List<IDomainEvent> events = context.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToList();
        foreach (IDomainEvent @event in events)
        {
            foreach (EventHandlerRegistration handler in _handlers.Where(h => h.EventType == @event.GetType()))
            {
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                await handler.Dispatch(scope.ServiceProvider, @event, new EventContext(Guid.CreateVersion7(now), handler.Consumer, now, now), ct);
            }
        }
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
