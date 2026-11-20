using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Infrastructure.Messaging;

public sealed partial class OutboxRelay : BackgroundService
{
    private const int LastErrorMaxLength = 2000;
    private const int CleanupBatchSize = 1000;
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BrokerRetryMin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BrokerRetryMax = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _services;
    private readonly Type _contextType;
    private readonly OutboxEvents _events;
    private readonly IReadOnlyList<EventHandlerRegistration> _handlers;
    private readonly OutboxOptions _options;
    private readonly OutboxMetrics _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxRelay> _logger;
    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    public OutboxRelay(
        IServiceProvider services,
        Type contextType,
        OutboxEvents events,
        IEnumerable<EventHandlerRegistration> handlers,
        IOptions<OutboxOptions> options,
        OutboxMetrics metrics,
        TimeProvider clock,
        ILogger<OutboxRelay> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _services = services;
        _contextType = contextType;
        _events = events;
        _handlers = handlers.ToList();
        _options = options.Value;
        _metrics = metrics;
        _clock = clock;
        _logger = logger;
    }

    public static Task DispatchAsync<TEvent, THandler>(IServiceProvider services, IDomainEvent @event, EventContext context, CancellationToken ct)
        where TEvent : class, IDomainEvent
        where THandler : class, IEventHandler<TEvent> =>
        services.GetRequiredService<THandler>().HandleAsync((TEvent)@event, context, ct);

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        DbContext db = (DbContext)scope.ServiceProvider.GetRequiredService(_contextType);

        int taken = await DeliverBatchAsync(db, ct);
        await RecordBacklogAsync(db, ct);
        await CleanUpAsync(db, ct);
        return taken;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RelayEnabled)
            return;

        using PeriodicTimer timer = new(_options.PollInterval, _clock);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogCycleFailed(error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> DeliverBatchAsync(DbContext db, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        List<OutboxMessage> batch = await db.Set<OutboxMessage>().FromSql(PendingBatch(db)).ToListAsync(ct);
        foreach (OutboxMessage message in batch)
        {
            if (!await DeliverAsync(message, ct))
                break;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count;
    }

    private FormattableString PendingBatch(DbContext db) =>
        FormattableStringFactory.Create(
            $$"""
            SELECT * FROM {{MappedTable.Of<OutboxMessage>(db)}}
            WHERE "ProcessedAt" IS NULL AND "FailedAt" IS NULL
              AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {0})
            ORDER BY "Id"
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """,
            _clock.GetUtcNow(),
            _options.BatchSize);

    private async Task<bool> DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        using Activity? activity = OutboxTracing.Source.StartActivity(
            ActivityKind.Consumer,
            links: LinkTo(message.TraceParent),
            name: $"outbox process {message.Type}");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", message.Type);
        activity?.SetTag("messaging.message.id", message.MessageId.ToString());

        try
        {
            OutboxEventType type = _events.Find(message.Type, message.Version)
                ?? throw new InvalidOperationException($"No event type is registered as {message.Type} v{message.Version}.");
            IDomainEvent @event = (IDomainEvent)(JsonSerializer.Deserialize(message.Payload, type.ClrType, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException($"Message {message.MessageId} has an empty payload."));
            DateTimeOffset deliveredAt = _clock.GetUtcNow();
            foreach (EventHandlerRegistration handler in _handlers.Where(h => h.EventType == type.ClrType))
            {
                await using AsyncServiceScope scope = _services.CreateAsyncScope();
                await handler.Dispatch(scope.ServiceProvider, @event, new EventContext(message.MessageId, handler.Consumer, message.OccurredAt, deliveredAt), ct);
            }

            DateTimeOffset processedAt = _clock.GetUtcNow();
            message.ProcessedAt = processedAt;
            _metrics.Delivered(processedAt - message.OccurredAt);
            return true;
        }
        catch (BrokerUnavailableException error) when (!ct.IsCancellationRequested)
        {
            Postpone(message, error);
            activity?.SetStatus(ActivityStatusCode.Error, error.GetType().Name);
            return false;
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            Fail(message, error);
            activity?.SetStatus(ActivityStatusCode.Error, error.GetType().Name);
            return true;
        }
    }

    private void Postpone(OutboxMessage message, BrokerUnavailableException error)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        message.LastError = Describe(error);
        TimeSpan age = now - message.OccurredAt;
        message.NextAttemptAt = now + (age < BrokerRetryMin ? BrokerRetryMin : age > BrokerRetryMax ? BrokerRetryMax : age);
        _metrics.DeliveryFailed(message.Type);
        LogBrokerUnavailable(error, message.MessageId, message.Type, message.NextAttemptAt.Value);
    }

    private void Fail(OutboxMessage message, Exception error)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        message.Attempts++;
        message.LastError = Describe(error);
        _metrics.DeliveryFailed(message.Type);

        if (message.Attempts >= _options.MaxAttempts)
        {
            message.FailedAt = now;
            message.NextAttemptAt = null;
            LogGaveUp(error, message.MessageId, message.Type, message.Attempts);
            return;
        }

        message.NextAttemptAt = now + TimeSpan.FromSeconds(Math.Pow(2, message.Attempts));
        LogWillRetry(error, message.MessageId, message.Type, message.Attempts, message.NextAttemptAt.Value);
    }

    private static string Describe(Exception error)
    {
        string description = error.InnerException is { } cause
            ? $"{error.GetType().Name}: {error.Message} ({cause.GetType().Name}: {cause.Message})"
            : $"{error.GetType().Name}: {error.Message}";
        return description.Length <= LastErrorMaxLength ? description : description[..LastErrorMaxLength];
    }

    private async Task RecordBacklogAsync(DbContext db, CancellationToken ct)
    {
        IQueryable<OutboxMessage> messages = db.Set<OutboxMessage>().AsNoTracking();
        IQueryable<OutboxMessage> pending = messages.Where(m => m.ProcessedAt == null && m.FailedAt == null);
        long pendingCount = await pending.LongCountAsync(ct);
        DateTimeOffset? oldest = await pending.MinAsync(m => (DateTimeOffset?)m.OccurredAt, ct);
        long failed = await messages.LongCountAsync(m => m.FailedAt != null, ct);
        _metrics.Backlog(pendingCount, oldest is null ? TimeSpan.Zero : _clock.GetUtcNow() - oldest.Value, failed);
    }

    private async Task CleanUpAsync(DbContext db, CancellationToken ct)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        if (now < _nextCleanupAt)
            return;

        _nextCleanupAt = now + CleanupEvery;
        DateTimeOffset cutoff = now - _options.Retention;
        await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt < cutoff)
            .OrderBy(m => m.Id)
            .Take(CleanupBatchSize)
            .ExecuteDeleteAsync(ct);
    }

    private static ActivityLink[] LinkTo(string? traceParent) =>
        ActivityContext.TryParse(traceParent, null, out ActivityContext context) ? [new ActivityLink(context)] : [];

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox relay cycle failed")]
    private partial void LogCycleFailed(Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed attempt {Attempts}; next attempt at {NextAttemptAt}")]
    private partial void LogWillRetry(Exception error, Guid messageId, string type, int attempts, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The broker did not take outbox message {MessageId} ({Type}); it stays pending without spending an attempt until {NextAttemptAt}")]
    private partial void LogBrokerUnavailable(Exception error, Guid messageId, string type, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) failed {Attempts} attempts and will not be retried")]
    private partial void LogGaveUp(Exception error, Guid messageId, string type, int attempts);
}
