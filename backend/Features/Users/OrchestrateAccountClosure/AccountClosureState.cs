namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public enum AccountClosureState
{
    HoldingTasks,
    Deciding,
    ReleasingTasks,
    Completed,
    Rejected,
}
