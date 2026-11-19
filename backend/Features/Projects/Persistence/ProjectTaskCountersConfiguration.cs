using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Projects.Domain;

namespace TemplateApp.Features.Projects.Persistence;

internal sealed class ProjectTaskCountersConfiguration : IEntityTypeConfiguration<ProjectTaskCounters>
{
    public void Configure(EntityTypeBuilder<ProjectTaskCounters> builder)
    {
        builder.HasKey(c => c.ProjectId);

        builder.HasOne<Project>()
            .WithOne()
            .HasForeignKey<ProjectTaskCounters>(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        builder.ToTable("ProjectTaskCounters");
    }
}
