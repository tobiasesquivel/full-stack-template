namespace TemplateApp.Features.Tasks;

internal static class TaskEvents
{
    public const string Exchange = "tasks.task-events";
    public const string Created = "tasks.task-created";
    public const string Completed = "tasks.task-completed";
    public const string Reopened = "tasks.task-reopened";
    public const string Deleted = "tasks.task-deleted";
    public const string OwnerTasksHeld = "tasks.owner-tasks-held";
    public const int Version = 1;
}
