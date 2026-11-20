using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Auth.Domain;

namespace TemplateApp.Features.Auth.Persistence;

internal sealed class AccountClosureConfiguration : IEntityTypeConfiguration<AccountClosure>
{
    public void Configure(EntityTypeBuilder<AccountClosure> builder)
    {
        builder.HasKey(c => c.ClosureId);

        builder.Ignore(c => c.DomainEvents);

        builder.Property(c => c.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(c => c.Outcome)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Reason)
            .HasMaxLength(100);

        builder.Property(c => c.DecidedAt)
            .IsRequired();

        builder.HasIndex(c => c.UserId);

        builder.ToTable("AccountClosures");
    }
}
