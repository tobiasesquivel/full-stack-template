using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Tasks.Domain;

namespace TemplateApp.Features.Tasks.Persistence;

internal sealed class OwnerHoldConfiguration : IEntityTypeConfiguration<OwnerHold>
{
    public void Configure(EntityTypeBuilder<OwnerHold> builder)
    {
        builder.HasKey(h => h.OwnerId);

        builder.Ignore(h => h.DomainEvents);

        builder.Property(h => h.OwnerId)
            .HasMaxLength(450);

        builder.Property(h => h.ClosureId)
            .IsRequired();

        builder.Property(h => h.HeldAt)
            .IsRequired();

        builder.ToTable("OwnerHolds");
    }
}
