using System.Diagnostics;

namespace TemplateApp.Features.Users.Telemetry;

internal static class AccountClosureTrace
{
    public const string Tag = "templateapp.account_closure.id";

    public static void Mark(Guid closureId) => Activity.Current?.SetTag(Tag, closureId.ToString());
}
