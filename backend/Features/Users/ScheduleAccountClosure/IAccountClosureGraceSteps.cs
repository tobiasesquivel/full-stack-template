namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public interface IAccountClosureGraceSteps
{
    Task<bool> RemindAsync(Guid closureId, string userId, int number, DateTimeOffset closesAt, CancellationToken ct);

    Task<IReadOnlyList<object>> StartClosureAsync(Guid closureId, string userId, CancellationToken ct);
}
