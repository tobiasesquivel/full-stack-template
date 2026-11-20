using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Auth.Domain;
using TemplateApp.Infrastructure.Messaging;

namespace TemplateApp.Features.Auth.Persistence;

internal sealed class AuthDbContext : IdentityDbContext<AppIdentityUser>
{
    public const string Schema = "auth";

    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<AccountClosure> AccountClosures => Set<AccountClosure>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.ApplyConfiguration(new SessionConfiguration());
        builder.ApplyConfiguration(new AccountClosureConfiguration());
        builder.MapOutbox();
        builder.MapInbox();
    }
}
