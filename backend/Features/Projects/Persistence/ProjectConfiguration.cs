using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Infrastructure.Persistence.Ownership;

namespace TemplateApp.Features.Projects.Persistence;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public const string UniqueNameIndex = "IX_Projects_OwnerId_NormalizedName";

    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);
        builder.ErasedWithOwner(p => p.OwnerId);

        builder.Property(p => p.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(Project.NameMaxLength);

        builder.Property(p => p.NormalizedName)
            .IsRequired()
            .HasMaxLength(Project.NameMaxLength);

        builder.Property(p => p.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.ArchivedAt);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        builder.Property(p => p.Version)
            .IsRowVersion();

        builder.HasIndex(p => new { p.OwnerId, p.NormalizedName })
            .IsUnique()
            .HasDatabaseName(UniqueNameIndex);

        builder.HasIndex(p => new { p.OwnerId, p.CreatedAt, p.Id });

        builder.ToTable("Projects", table => table.HasCheckConstraint(
            "CK_Projects_State",
            $"\"State\" IN ('{nameof(ProjectState.Active)}', '{nameof(ProjectState.Archived)}')"));
    }
}
