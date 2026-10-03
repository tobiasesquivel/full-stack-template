namespace TemplateApp.Domain.Entities;

public class TaskItem
{
    public Guid Id { get; init; }
    public required string AuthId { get; init; }
    public required string Title { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; set; }
}
