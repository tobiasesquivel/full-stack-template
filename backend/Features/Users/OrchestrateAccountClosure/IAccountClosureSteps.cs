namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public interface IAccountClosureSteps
{
    Task CompleteAsync(Guid closureId, string userId, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken ct);

    Task RejectAsync(Guid closureId, string userId, string reason, DateTimeOffset startedAt, DateTimeOffset now, CancellationToken ct);

    void MarkOverdue(string state);
}
