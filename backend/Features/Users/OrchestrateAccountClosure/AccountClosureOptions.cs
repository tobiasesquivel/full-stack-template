using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

internal sealed record AccountClosureOptions
{
    public const string Section = "AccountClosure";

    [Range(1, 604800)]
    public int OverdueAfterSeconds { get; init; } = 900;

    public TimeSpan OverdueAfter => TimeSpan.FromSeconds(OverdueAfterSeconds);
}
