using Microsoft.EntityFrameworkCore;
using TemplateApp.Infrastructure.Persistence.Audit;
using TemplateApp.Infrastructure.Persistence.Idempotency;

namespace TemplateApp.Infrastructure.Persistence;

public sealed class PlatformDbContext : DbContext
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
