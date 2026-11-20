using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TemplateApp.Features.Users.OrchestrateAccountClosure;

namespace TemplateApp.Features.Users.Persistence;

internal sealed class AccountClosureSagaConfiguration : IEntityTypeConfiguration<AccountClosureSaga>
{
    public void Configure(EntityTypeBuilder<AccountClosureSaga> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("ClosureId")
            .ValueGeneratedNever();

        builder.Property(s => s.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(s => s.State)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Reason)
            .HasMaxLength(100);

        builder.Property(s => s.Version)
            .IsConcurrencyToken();

        builder.HasIndex(s => s.UserId);

        builder.ToTable("AccountClosureSagas");
    }
}
