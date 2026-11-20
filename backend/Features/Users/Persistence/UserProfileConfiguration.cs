using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Features.Users.Persistence;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(u => u.AuthId);

        builder.Ignore(u => u.DomainEvents);

        builder.Property(u => u.AuthId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(UserProfile.DisplayNameMaxLength)
            .IsRequired();

        builder.Property(u => u.Timezone)
            .HasMaxLength(100)
            .IsRequired()
            .HasDefaultValue("UTC");

        builder.Property(u => u.ClosureStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(AccountClosureStatus.None);

        builder.Property(u => u.ClosureReason)
            .HasMaxLength(100);

        builder.ToTable("UserProfiles");
    }
}
