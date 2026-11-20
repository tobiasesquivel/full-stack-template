namespace TemplateApp.Features.Users.Contracts;

public sealed record UserProfileDetails(string DisplayName, string Timezone, string AccountStatus, UserProfileDetails.ClosureDetails? LastClosure)
{
    public sealed record ClosureDetails(Guid ClosureId, string Outcome, string? Reason, DateTimeOffset DecidedAt);
}
