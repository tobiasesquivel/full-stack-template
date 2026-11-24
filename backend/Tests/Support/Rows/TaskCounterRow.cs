namespace TemplateApp.Tests.Support.Rows;

internal sealed record TaskCounterRow(Guid ProjectId, int TaskCount, int CompletedTaskCount);
