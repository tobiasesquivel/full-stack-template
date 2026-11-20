using Wolverine;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

public sealed record AccountClosureOverdue(Guid ClosureId, TimeSpan After) : TimeoutMessage(After);
