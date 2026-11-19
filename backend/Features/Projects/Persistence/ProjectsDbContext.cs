using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Projects.Domain;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Projects.Persistence;

internal sealed class ProjectsDbContext : DbContext
{
    public const string Schema = "projects";

    public ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectTaskCounters> TaskCounters => Set<ProjectTaskCounters>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectTaskCountersConfiguration());
        modelBuilder.MapInbox();
        modelBuilder.MapSharedAuditEntries();
    }
}
