using Wolverine;

namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public sealed record AccountClosureGraceExpired(Guid ClosureId, TimeSpan After) : TimeoutMessage(After);
