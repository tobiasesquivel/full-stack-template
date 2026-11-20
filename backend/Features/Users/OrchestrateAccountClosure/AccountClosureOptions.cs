using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

internal sealed record AccountClosureOptions : IValidatableObject
{
    public const string Section = "AccountClosure";

    private static readonly int[] DefaultReminderOffsetsSeconds = [604800, 1123200];

    [Range(1, 604800)]
    public int OverdueAfterSeconds { get; init; } = 900;

    [Range(1, 31536000)]
    public int GracePeriodSeconds { get; init; } = 1209600;

    public int[]? ReminderOffsetsSeconds { get; init; }

    public TimeSpan OverdueAfter => TimeSpan.FromSeconds(OverdueAfterSeconds);

    public TimeSpan GracePeriod => TimeSpan.FromSeconds(GracePeriodSeconds);

    public IReadOnlyList<TimeSpan> Reminders => [.. (ReminderOffsetsSeconds ?? DefaultReminderOffsetsSeconds).Select(seconds => TimeSpan.FromSeconds(seconds))];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        int[] offsets = ReminderOffsetsSeconds ?? DefaultReminderOffsetsSeconds;
        for (int i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] <= 0 || offsets[i] >= GracePeriodSeconds)
                return [new ValidationResult($"{Section}:ReminderOffsetsSeconds must be between 1 and GracePeriodSeconds - 1; reminder {i + 1} is {offsets[i]}.")];

            if (i > 0 && offsets[i] <= offsets[i - 1])
                return [new ValidationResult($"{Section}:ReminderOffsetsSeconds must grow: reminder {i + 1} ({offsets[i]}) is not after reminder {i} ({offsets[i - 1]}).")];
        }

        return [];
    }
}
