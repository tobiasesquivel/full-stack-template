using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TemplateApp.Infrastructure.Persistence;

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.HasKey(k => new { k.OwnerId, k.Key });

        builder.Property(k => k.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(k => k.Key)
            .HasColumnType("uuid");

        builder.Property(k => k.Fingerprint)
            .IsRequired();

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.HasIndex(k => k.CreatedAt);

        builder.ToTable("IdempotencyKeys");
    }
}
