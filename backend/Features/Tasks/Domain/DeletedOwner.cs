namespace TemplateApp.Features.Tasks.Domain;

internal sealed class DeletedOwner
{
    public required string OwnerId { get; init; }
    public DateTimeOffset DeletedAt { get; init; }
}
