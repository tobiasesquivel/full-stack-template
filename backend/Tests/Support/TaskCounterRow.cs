namespace TemplateApp.Tests.Support;

internal sealed record TaskCounterRow(Guid ProjectId, int TaskCount, int CompletedTaskCount);
