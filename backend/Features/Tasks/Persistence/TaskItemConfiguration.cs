using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Tasks.Domain;

namespace TemplateApp.Features.Tasks.Persistence;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Ignore(t => t.DomainEvents);

        builder.Property(t => t.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(TaskItem.TitleMaxLength);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt);

        builder.Property(t => t.CompletedAt);

        builder.Property(t => t.Version)
            .IsRowVersion();

        builder.Property(t => t.ProjectId);

        builder.HasIndex(t => new { t.OwnerId, t.CreatedAt, t.Id });

        builder.HasIndex(t => t.ProjectId)
            .IsCreatedConcurrently();

        builder.ToTable("TaskItems");
    }
}
