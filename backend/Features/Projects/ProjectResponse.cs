namespace TemplateApp.Features.Projects;

internal sealed record ProjectResponse(
    Guid Id,
    string Name,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version,
    string OwnerName,
    int TaskCount,
    int CompletedTaskCount);
