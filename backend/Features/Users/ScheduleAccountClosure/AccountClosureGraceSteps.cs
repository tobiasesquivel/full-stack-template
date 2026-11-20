namespace TemplateApp.Features.Users.ScheduleAccountClosure;

internal sealed class AccountClosureGraceSteps : IAccountClosureGraceSteps
{
    private readonly RemindAccountClosure.RemindAccountClosure _remind;
    private readonly StartAccountClosure.StartAccountClosure _start;

    public AccountClosureGraceSteps(RemindAccountClosure.RemindAccountClosure remind, StartAccountClosure.StartAccountClosure start)
    {
        _remind = remind;
        _start = start;
    }

    public Task<bool> RemindAsync(Guid closureId, string userId, int number, DateTimeOffset closesAt, CancellationToken ct) =>
        _remind.RemindAsync(closureId, userId, number, closesAt, ct);

    public Task<IReadOnlyList<object>> StartClosureAsync(Guid closureId, string userId, CancellationToken ct) =>
        _start.StartAsync(closureId, userId, ct);
}
