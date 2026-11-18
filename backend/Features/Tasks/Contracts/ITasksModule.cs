namespace TemplateApp.Features.Tasks.Contracts;

public interface ITasksModule
{
    Task DeleteOwnedTasksAsync(string ownerId, CancellationToken ct);
}
