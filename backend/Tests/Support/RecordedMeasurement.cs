namespace TemplateApp.Tests.Support;

internal sealed record RecordedMeasurement(double Value, IReadOnlyDictionary<string, object?> Tags);
