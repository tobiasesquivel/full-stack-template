using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Features.Users.Domain;
using TemplateApp.Infrastructure.Identity;

namespace TemplateApp.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<AppIdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
