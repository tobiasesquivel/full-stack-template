using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Domain.Entities;

namespace TemplateApp.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Token)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(rt => rt.JwtId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(rt => rt.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(rt => rt.ExpiresAt).IsRequired();
        builder.Property(rt => rt.CreatedAt).IsRequired();
        builder.Property(rt => rt.RevokedAt);
        builder.Property(rt => rt.ReplacedByToken).HasMaxLength(256);

        builder.HasIndex(rt => rt.Token).IsUnique();
        builder.HasIndex(rt => new { rt.UserId, rt.RevokedAt });

        builder.HasOne<AppIdentityUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
