using System.Diagnostics.Metrics;

namespace TemplateApp.Features.Users;

internal sealed class UserMetrics
{
    private const string MeterName = "TemplateApp.Users";

    private const string OutcomeTag = "templateapp.account_closure.outcome";

    private const string StateTag = "templateapp.account_closure.state";

    private static readonly double[] DurationBucketsSeconds = [0.1, 0.5, 1, 2, 5, 10, 30, 60, 300, 3600, 86400];

    private readonly Counter<long> _finished;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _overdue;

    public UserMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        _finished = meter.CreateCounter<long>(
            "templateapp.account_closures.finished",
            unit: "{closure}",
            description: "Account closures that finished, by outcome.");
        _duration = meter.CreateHistogram(
            "templateapp.account_closures.duration",
            unit: "s",
            description: "Time from the closure request until Users saved its end.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBucketsSeconds });
        _overdue = meter.CreateCounter<long>(
            "templateapp.account_closures.overdue",
            unit: "{closure}",
            description: "Account closures still in flight when their deadline passed, by the state they were stuck in.");
    }

    public void AccountClosureFinished(ClosureOutcome outcome, TimeSpan duration)
    {
        KeyValuePair<string, object?> tag = new(OutcomeTag, TagValue(outcome));
        _finished.Add(1, tag);
        _duration.Record(duration.TotalSeconds, tag);
    }

    public void AccountClosureOverdue(string state) => _overdue.Add(1, new KeyValuePair<string, object?>(StateTag, state));

    private static string TagValue(ClosureOutcome outcome) => outcome switch
    {
        ClosureOutcome.Completed => "completed",
        ClosureOutcome.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown closure outcome."),
    };
}
