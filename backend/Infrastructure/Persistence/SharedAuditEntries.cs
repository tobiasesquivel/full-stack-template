using Microsoft.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Persistence;

public static class SharedAuditEntries
{
    public static ModelBuilder MapSharedAuditEntries(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
        modelBuilder.Entity<AuditEntry>().ToTable(t => t.ExcludeFromMigrations());
        return modelBuilder;
    }
}
