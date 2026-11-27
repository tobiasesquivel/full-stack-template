using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Messaging.Outbox;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Infrastructure.Persistence.Ownership;

namespace TemplateApp.Tests.Support.Contexts;

internal sealed class ProbeDbContext : DbContext
{
    public const string Schema = "probe";

    public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options)
    {
    }

    public DbSet<ProbeItem> Items => Set<ProbeItem>();

    public static void Register(IServiceCollection services)
    {
        services.AddMigratedDbContext<ProbeDbContext>(Schema, adoptsLegacyTables: false, appliesMigrations: false);
        services.AddOutbox<ProbeDbContext>();
        services.AddOutboxEvent<ProbeItemCreated>("probe.item-created", 1);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<ProbeItem>(item =>
        {
            item.ToTable("ProbeItems");
            item.HasKey(i => i.Id);
            item.Ignore(i => i.DomainEvents);
            item.Property(i => i.HolderId).HasMaxLength(450);
            item.ErasedWithOwner(i => i.HolderId);
        });
        modelBuilder.MapOutbox();
    }
}
