using System.Diagnostics.Metrics;

namespace TemplateApp.Infrastructure.Messaging.Outbox;

public sealed class OutboxMetrics
{
    public const string MeterName = "TemplateApp.Outbox";
    public const string TypeTag = "templateapp.outbox.type";

    private static readonly double[] LagBucketsSeconds = [0.1, 0.25, 0.5, 1, 2, 5, 10, 30, 60, 300, 3600];

    private readonly Gauge<long> _pending;
    private readonly Gauge<double> _oldestPendingAge;
    private readonly Histogram<double> _lag;
    private readonly Counter<long> _failures;
    private readonly Gauge<long> _failed;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        Meter meter = meterFactory.Create(MeterName);
        _pending = meter.CreateGauge<long>(
            "templateapp.outbox.pending",
            unit: "{message}",
            description: "Messages waiting in the outbox when the last relay cycle ended.");
        _oldestPendingAge = meter.CreateGauge<double>(
            "templateapp.outbox.oldest_pending.age",
            unit: "s",
            description: "How long the oldest pending message has waited.");
        _lag = meter.CreateHistogram(
            "templateapp.outbox.delivery.lag",
            unit: "s",
            description: "Time from saving a message until the relay delivered it.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = LagBucketsSeconds });
        _failures = meter.CreateCounter<long>(
            "templateapp.outbox.delivery.failures",
            unit: "{attempt}",
            description: "Delivery attempts that failed.");
        _failed = meter.CreateGauge<long>(
            "templateapp.outbox.failed",
            unit: "{message}",
            description: "Messages that ran out of attempts and wait for someone to look at them.");
    }

    public void Delivered(TimeSpan lag) => _lag.Record(lag.TotalSeconds);

    public void DeliveryFailed(string type) => _failures.Add(1, new KeyValuePair<string, object?>(TypeTag, type));

    public void Backlog(long pending, TimeSpan oldestPendingAge, long failed)
    {
        _pending.Record(pending);
        _oldestPendingAge.Record(oldestPendingAge.TotalSeconds);
        _failed.Record(failed);
    }
}
