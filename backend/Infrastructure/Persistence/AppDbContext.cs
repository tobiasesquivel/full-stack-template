using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Users.Domain;

namespace TemplateApp.Infrastructure.Persistence;

internal class AppDbContext : IdentityDbContext<AppIdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ConfigureCrossFeatureRelationships(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureCrossFeatureRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>()
            .HasOne<AppIdentityUser>()
            .WithOne()
            .HasForeignKey<UserProfile>(u => u.AuthId)
            .HasConstraintName("FK_UserProfiles_AspNetUsers_AuthId")
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskItem>()
            .HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IdempotencyKey>()
            .HasOne<UserProfile>()
            .WithMany()
            .HasForeignKey(k => k.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
