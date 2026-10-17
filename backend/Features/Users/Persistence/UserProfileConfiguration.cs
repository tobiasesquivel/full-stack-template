using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users.Persistence;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(u => u.AuthId);

        builder.Property(u => u.AuthId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(u => u.Timezone)
            .HasMaxLength(100)
            .IsRequired()
            .HasDefaultValue("UTC");

        builder.HasOne<AppIdentityUser>()
            .WithOne()
            .HasForeignKey<UserProfile>(u => u.AuthId)
            .HasConstraintName("FK_UserProfiles_AspNetUsers_AuthId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("UserProfiles");
    }
}
