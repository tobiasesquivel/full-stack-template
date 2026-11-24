namespace TemplateApp.Tests.Support.Probes;

internal sealed record RecordedMeasurement(double Value, IReadOnlyDictionary<string, object?> Tags);
