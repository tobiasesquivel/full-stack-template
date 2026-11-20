using Wolverine;

namespace TemplateApp.Features.Users.ScheduleAccountClosure;

public sealed record AccountClosureReminderDue(Guid ClosureId, int Number, TimeSpan After) : TimeoutMessage(After);
