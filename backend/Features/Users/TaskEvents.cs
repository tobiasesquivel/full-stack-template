namespace TemplateApp.Features.Users;

internal static class TaskEvents
{
    public const string Exchange = "tasks.task-events";
    public const string OwnerTasksHeld = "tasks.owner-tasks-held";
    public const string OwnerTasksReleased = "tasks.owner-tasks-released";
    public const int Version = 1;
}
