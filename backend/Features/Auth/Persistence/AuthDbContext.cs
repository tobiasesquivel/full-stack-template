using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Domain;

namespace TemplateApp.Features.Auth.Persistence;

internal sealed class AuthDbContext : IdentityDbContext<AppIdentityUser>
{
    public const string Schema = "auth";

    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.ApplyConfiguration(new SessionConfiguration());
    }
}
