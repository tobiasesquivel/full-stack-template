using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TemplateApp.Infrastructure.Messaging;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.HasKey(m => new { m.MessageId, m.Consumer });

        builder.Property(m => m.Consumer)
            .HasMaxLength(256);

        builder.Property(m => m.ProcessedAt)
            .IsRequired();

        builder.ToTable("InboxMessages");
    }
}
