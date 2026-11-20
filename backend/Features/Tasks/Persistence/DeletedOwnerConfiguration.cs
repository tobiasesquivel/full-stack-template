using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Tasks.Domain;

namespace TemplateApp.Features.Tasks.Persistence;

internal sealed class DeletedOwnerConfiguration : IEntityTypeConfiguration<DeletedOwner>
{
    public void Configure(EntityTypeBuilder<DeletedOwner> builder)
    {
        builder.HasKey(o => o.OwnerId);

        builder.Property(o => o.OwnerId)
            .HasMaxLength(450);

        builder.Property(o => o.DeletedAt)
            .IsRequired();

        builder.ToTable("DeletedOwners");
    }
}
