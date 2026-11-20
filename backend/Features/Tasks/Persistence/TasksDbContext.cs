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

    public DbSet<OwnerHold> OwnerHolds => Set<OwnerHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new TaskItemConfiguration());
        modelBuilder.ApplyConfiguration(new DeletedOwnerConfiguration());
        modelBuilder.ApplyConfiguration(new OwnerHoldConfiguration());
        modelBuilder.MapSharedAuditEntries();
        modelBuilder.MapOutbox();
        modelBuilder.MapInbox();
    }
}
