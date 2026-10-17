using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Tasks.Persistence;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(TaskItem.TitleMaxLength);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt);

        builder.HasIndex(t => t.OwnerId);

        builder.HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("TaskItems");
    }
}
