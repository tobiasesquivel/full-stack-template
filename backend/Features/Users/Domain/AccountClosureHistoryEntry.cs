using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Users.Domain;

internal sealed class AccountClosureHistoryEntry : IAppendOnly
{
    public const int KindMaxLength = 30;
    public const int DetailMaxLength = 100;

    public long Id { get; init; }
    public required Guid ClosureId { get; init; }
    public required string UserId { get; init; }
    public required string Kind { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Actor { get; init; }
    public string? Detail { get; init; }
}
