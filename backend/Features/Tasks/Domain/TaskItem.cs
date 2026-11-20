using TemplateApp.Features.Tasks.Domain.Events;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain;

internal sealed class TaskItem : IOwnedResource, IAuditable, IHasDomainEvents
{
    public const int TitleMaxLength = 200;

    private readonly List<IDomainEvent> _domainEvents = [];

    private TaskItem()
    {
    }

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public static TaskItem Create(LiveOwner owner, string title, Guid? projectId, DateTimeOffset now)
    {
        TaskItem item = new()
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Title = title,
            ProjectId = projectId,
            CreatedAt = now,
        };
        item._domainEvents.Add(new TaskCreated(item.Id, owner.Id, projectId, now));
        return item;
    }

    public void Update(string title, bool isCompleted, DateTimeOffset now)
    {
        Title = title;
        if (isCompleted && CompletedAt is null)
        {
            CompletedAt = now;
            _domainEvents.Add(new TaskCompleted(Id, OwnerId, ProjectId, now));
        }
        else if (!isCompleted && CompletedAt is not null)
        {
            CompletedAt = null;
            _domainEvents.Add(new TaskReopened(Id, OwnerId, ProjectId, now));
        }

        UpdatedAt = now;
    }

    public void MarkDeleted() =>
        _domainEvents.Add(new TaskDeleted(Id, OwnerId, ProjectId, CompletedAt is not null));

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static bool IsValidTitle(string title) =>
        NormalizeTitle(title).Length is >= 1 and <= TitleMaxLength;

    public static string NormalizeTitle(string title) => title.Trim();
}
