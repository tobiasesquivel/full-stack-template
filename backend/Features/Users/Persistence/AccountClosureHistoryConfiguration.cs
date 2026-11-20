using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users.Persistence;

internal sealed class AccountClosureHistoryConfiguration : IEntityTypeConfiguration<AccountClosureHistoryEntry>
{
    public void Configure(EntityTypeBuilder<AccountClosureHistoryEntry> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .UseIdentityAlwaysColumn();

        builder.Property(e => e.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasMaxLength(AccountClosureHistoryEntry.KindMaxLength)
            .IsRequired();

        builder.Property(e => e.Actor)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(e => e.Detail)
            .HasMaxLength(AccountClosureHistoryEntry.DetailMaxLength);

        builder.HasIndex(e => new { e.ClosureId, e.Kind, e.Detail })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.ToTable("AccountClosureHistory");
    }
}
