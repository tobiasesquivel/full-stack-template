using System.Diagnostics.Metrics;

namespace TemplateApp.Features.Tasks.Telemetry;

internal sealed class TaskMetrics
{
    private const string MeterName = "TemplateApp.Tasks";

    private static readonly double[] CompletionBucketsSeconds = [60, 300, 900, 3600, 14400, 86400, 259200, 604800, 2592000];

    private readonly Counter<long> _created;
    private readonly Histogram<double> _completionDuration;

    public TaskMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>(
            "templateapp.tasks.created",
            unit: "{task}",
            description: "Tasks created and saved.");
        _completionDuration = meter.CreateHistogram(
            "templateapp.tasks.completion.duration",
            unit: "s",
            description: "Time from a task's creation until it is saved as completed.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = CompletionBucketsSeconds });
    }

    public void TaskCreated() => _created.Add(1);

    public void TaskCompleted(TimeSpan sinceCreation) => _completionDuration.Record(sinceCreation.TotalSeconds);
}
