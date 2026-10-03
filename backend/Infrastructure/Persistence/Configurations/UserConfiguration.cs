using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Domain.Entities;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.AuthId);

        builder.Property(u => u.AuthId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(u => u.Username)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne<AppIdentityUser>()
            .WithOne()
            .HasForeignKey<User>(u => u.AuthId)
            .HasConstraintName("FK_Users_AspNetUsers_AuthId");
    }
}
