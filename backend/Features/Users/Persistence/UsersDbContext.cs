using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Users.Persistence;

internal sealed class UsersDbContext : DbContext
{
    public const string Schema = "users";

    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<OrchestrateAccountClosure.AccountClosureSaga> AccountClosureSagas => Set<OrchestrateAccountClosure.AccountClosureSaga>();

    public DbSet<ScheduleAccountClosure.AccountClosureGracePeriod> AccountClosureGracePeriods => Set<ScheduleAccountClosure.AccountClosureGracePeriod>();

    public DbSet<AccountClosureHistoryEntry> AccountClosureHistory => Set<AccountClosureHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new UserProfileConfiguration());
        modelBuilder.ApplyConfiguration(new AccountClosureSagaConfiguration());
        modelBuilder.ApplyConfiguration(new AccountClosureGracePeriodConfiguration());
        modelBuilder.ApplyConfiguration(new AccountClosureHistoryConfiguration());
        modelBuilder.MapSharedAuditEntries();
        modelBuilder.MapOutbox();
        modelBuilder.MapInbox();
    }
}
