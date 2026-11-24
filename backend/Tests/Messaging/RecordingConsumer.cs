using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Features.Tasks.Persistence;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Inbox;
using TemplateApp.Infrastructure.Messaging.Outbox;

namespace TemplateApp.Tests.Messaging;

internal sealed class RecordingConsumer : IEventHandler<TaskCreated>
{
    private readonly ConsumerProbe _probe;
    private readonly TasksDbContext _db;

    public RecordingConsumer(ConsumerProbe probe, TasksDbContext db)
    {
        _probe = probe;
        _db = db;
    }

    public async Task HandleAsync(TaskCreated @event, EventContext context, CancellationToken ct)
    {
        _probe.Called(context.MessageId);
        if (_probe.Fails)
            throw new InvalidOperationException("The test consumer is down.");

        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);
        if (_probe.UsesInbox && !await _db.TryAcceptAsync(context, ct))
            return;

        await _db.Database.ExecuteSqlAsync(
            $"""INSERT INTO tasks."OutboxTestEffects" ("MessageId", "TaskId") VALUES ({context.MessageId}, {@event.TaskId})""",
            ct);
        await transaction.CommitAsync(ct);
    }
}
