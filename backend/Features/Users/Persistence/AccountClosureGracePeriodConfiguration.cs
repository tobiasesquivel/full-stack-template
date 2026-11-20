using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Users.ScheduleAccountClosure;

namespace TemplateApp.Features.Users.Persistence;

internal sealed class AccountClosureGracePeriodConfiguration : IEntityTypeConfiguration<AccountClosureGracePeriod>
{
    public void Configure(EntityTypeBuilder<AccountClosureGracePeriod> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("ClosureId")
            .ValueGeneratedNever();

        builder.Property(s => s.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(s => s.Version)
            .IsConcurrencyToken();

        builder.HasIndex(s => s.UserId);

        builder.ToTable("AccountClosureGracePeriods");
    }
}
