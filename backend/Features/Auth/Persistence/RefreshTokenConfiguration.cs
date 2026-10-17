using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;

namespace TemplateApp.Features.Auth.Persistence;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(rt => rt.FamilyId)
            .IsRequired();

        builder.Property(rt => rt.UserId)
            .IsRequired()
            .HasMaxLength(450)
            .HasColumnName("AppIdentityUserId");
        builder.Property(rt => rt.ExpiresAt).IsRequired();
        builder.Property(rt => rt.CreatedAt).IsRequired();
        builder.Property(rt => rt.RevokedAt);
        builder.Property(rt => rt.ReplacedByTokenHash).HasMaxLength(256);

        builder.HasIndex(rt => rt.TokenHash).IsUnique();
        builder.HasIndex(rt => rt.FamilyId);
        builder.HasIndex(rt => new { rt.UserId, rt.RevokedAt });

        builder.HasOne<AppIdentityUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .HasConstraintName("FK_RefreshTokens_AspNetUsers_AppIdentityUserId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
