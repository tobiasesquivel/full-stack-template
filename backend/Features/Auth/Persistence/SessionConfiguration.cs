using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Infrastructure.Persistence.Ownership;

namespace TemplateApp.Features.Auth.Persistence;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.HasKey(s => s.Id);
        builder.ErasedWithOwner(s => s.UserId);

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(450);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.LastSeenAt).IsRequired();

        builder.HasIndex(s => s.UserId);

        builder.HasOne<AppIdentityUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("FK_Sessions_AspNetUsers_UserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
