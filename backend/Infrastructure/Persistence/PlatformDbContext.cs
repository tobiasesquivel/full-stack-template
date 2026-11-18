using Microsoft.EntityFrameworkCore;

namespace TemplateApp.Infrastructure.Persistence;

internal sealed class PlatformDbContext : DbContext
{
    public const string Schema = "platform";

    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options)
    {
    }

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new IdempotencyKeyConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
    }
}
