using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TemplateApp.Infrastructure.Messaging;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const int TypeMaxLength = 128;
    public const int TraceParentLength = 55;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MessageId)
            .IsRequired();

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(TypeMaxLength);

        builder.Property(m => m.Version)
            .IsRequired();

        builder.Property(m => m.Payload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(m => m.OccurredAt)
            .IsRequired();

        builder.Property(m => m.TraceParent)
            .HasMaxLength(TraceParentLength);

        builder.Property(m => m.Attempts)
            .HasDefaultValue(0);

        builder.HasIndex(m => m.MessageId)
            .IsUnique();

        builder.HasIndex(m => m.Id)
            .HasDatabaseName("IX_OutboxMessages_Pending")
            .HasFilter("\"ProcessedAt\" IS NULL AND \"FailedAt\" IS NULL");

        builder.ToTable("OutboxMessages");
    }
}
