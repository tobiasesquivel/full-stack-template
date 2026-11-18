using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Projects.Domain;

internal sealed class Project : IOwnedResource, IAuditable
{
    public const int NameMaxLength = 100;

    private Project()
    {
    }

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public ProjectState State { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsArchived => State == ProjectState.Archived;

    public static Project Create(string ownerId, string name, DateTimeOffset now)
    {
        string trimmed = ValidName(name);
        return new Project
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = trimmed,
            NormalizedName = NormalizeKey(trimmed),
            State = ProjectState.Active,
            CreatedAt = now,
        };
    }

    public ProjectChange Rename(string name, DateTimeOffset now)
    {
        string trimmed = ValidName(name);
        if (Name == trimmed)
            return ProjectChange.Unchanged;

        if (IsArchived)
            return ProjectChange.RejectedArchived;

        Name = trimmed;
        NormalizedName = NormalizeKey(trimmed);
        UpdatedAt = now;
        return ProjectChange.Changed;
    }

    public ProjectChange Archive(DateTimeOffset now)
    {
        if (IsArchived)
            return ProjectChange.Unchanged;

        State = ProjectState.Archived;
        ArchivedAt = now;
        UpdatedAt = now;
        return ProjectChange.Changed;
    }

    public ProjectChange Restore(DateTimeOffset now)
    {
        if (!IsArchived)
            return ProjectChange.Unchanged;

        State = ProjectState.Active;
        ArchivedAt = null;
        UpdatedAt = now;
        return ProjectChange.Changed;
    }

    public static bool IsValidName(string name) =>
        NormalizeName(name).Length is >= 1 and <= NameMaxLength;

    public static string NormalizeName(string name) => name.Trim();

    public static string NormalizeKey(string name) => NormalizeName(name).ToUpperInvariant();

    private static string ValidName(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException($"A project name has between 1 and {NameMaxLength} characters.", nameof(name));

        return NormalizeName(name);
    }
}
