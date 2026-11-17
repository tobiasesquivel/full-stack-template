using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace TemplateApp.Tests.Support;

internal sealed class RecordedMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<RecordedMeasurement> _values = new();

    public RecordedMeasurements(IServiceProvider services, string meterName, string instrumentName)
    {
        IMeterFactory scope = services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, scope)
                && instrument.Meter.Name == meterName
                && instrument.Name == instrumentName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Record(value, tags));
        _listener.SetMeasurementEventCallback<double>((_, value, tags, _) => Record(value, tags));
        _listener.Start();
    }

    public IReadOnlyList<RecordedMeasurement> Values => _values.ToArray();

    private void Record(double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> copy = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> tag in tags)
            copy[tag.Key] = tag.Value;
        _values.Enqueue(new RecordedMeasurement(value, copy));
    }

    public void Dispose() => _listener.Dispose();
}
