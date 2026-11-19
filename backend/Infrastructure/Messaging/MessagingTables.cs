using Microsoft.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Messaging;

public static class MessagingTables
{
    public static ModelBuilder MapOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        return modelBuilder;
    }

    public static ModelBuilder MapInbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        return modelBuilder;
    }
}
