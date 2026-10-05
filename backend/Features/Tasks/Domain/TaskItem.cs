using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain;

public sealed class TaskItem : IOwnedResource
{
    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Title { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
