namespace TemplateApp.Features.Projects.Persistence;

internal sealed class ProjectTaskCounters
{
    public Guid ProjectId { get; set; }

    public int TaskCount { get; set; }

    public int CompletedTaskCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
