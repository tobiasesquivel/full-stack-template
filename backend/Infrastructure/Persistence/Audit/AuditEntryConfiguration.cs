using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TemplateApp.Infrastructure.Persistence.Audit;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OccurredAt)
            .IsRequired();

        builder.Property(a => a.ActorKind)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(a => a.ActorId)
            .HasMaxLength(450);

        builder.Property(a => a.EntityType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(a => a.Operation)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(a => a.ChangedProperties)
            .IsRequired();

        builder.Property(a => a.TraceId)
            .HasMaxLength(32);

        builder.HasIndex(a => a.OccurredAt);

        builder.ToTable("AuditEntries");
    }
}
