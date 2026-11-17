using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.Domain;

public sealed class TaskItem : IOwnedResource, IAuditable
{
    public const int TitleMaxLength = 200;

    public Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required string Title { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public uint Version { get; set; }

    public static bool IsValidTitle(string title) =>
        NormalizeTitle(title).Length is >= 1 and <= TitleMaxLength;

    public static string NormalizeTitle(string title) => title.Trim();
}
