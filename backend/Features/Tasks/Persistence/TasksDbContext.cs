using Microsoft.EntityFrameworkCore;
using TemplateApp.Features.Tasks.Domain;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Tasks.Persistence;

internal sealed class TasksDbContext : DbContext
{
    public const string Schema = "tasks";

    public TasksDbContext(DbContextOptions<TasksDbContext> options) : base(options)
    {
    }

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<DeletedOwner> DeletedOwners => Set<DeletedOwner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
        modelBuilder.ApplyConfiguration(new DeletedOwnerConfiguration());
        modelBuilder.MapSharedAuditEntries();
        modelBuilder.MapOutbox();
        modelBuilder.MapInbox();
    }
}
