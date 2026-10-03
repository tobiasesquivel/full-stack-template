using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Domain.Entities;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.AuthId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt);

        builder.HasIndex(t => t.AuthId);

        builder.HasOne<AppIdentityUser>()
            .WithMany()
            .HasForeignKey(t => t.AuthId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
