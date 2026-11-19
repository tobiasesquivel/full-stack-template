using System.ComponentModel.DataAnnotations;

namespace TemplateApp.Infrastructure.Messaging;

public sealed record OutboxOptions
{
    public const string Section = "Outbox";

    public bool RelayEnabled { get; init; } = true;

    [Range(10, 600000)]
    public int PollIntervalMilliseconds { get; init; } = 1000;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    [Range(1, 3650)]
    public int RetentionDays { get; init; } = 7;

    public TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollIntervalMilliseconds);

    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);
}
