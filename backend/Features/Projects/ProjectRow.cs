using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Projects;

internal sealed record ProjectRow(
    Guid Id,
    string OwnerId,
    string Name,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    uint Version) : IOwnedResource;
