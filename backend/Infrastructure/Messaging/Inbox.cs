using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Messaging;

public static class Inbox
{
    public static async Task<bool> TryAcceptAsync(this DbContext db, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(context);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The inbox row and the effect of the consumer share one transaction: open it with Database.BeginTransactionAsync first.");

        string table = MappedTable.Of<InboxMessage>(db);
        int inserted = await db.Database.ExecuteSqlAsync(
            FormattableStringFactory.Create(
                $$"""
                INSERT INTO {{table}} ("MessageId", "Consumer", "ProcessedAt")
                VALUES ({0}, {1}, {2})
                ON CONFLICT ("MessageId", "Consumer") DO NOTHING
                """,
                context.MessageId,
                context.Consumer,
                context.DeliveredAt),
            ct);
        return inserted == 1;
    }
}
