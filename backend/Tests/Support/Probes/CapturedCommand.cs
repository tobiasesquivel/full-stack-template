using Npgsql;

namespace TemplateApp.Tests.Support.Probes;

internal sealed record CapturedCommand(string Text, IReadOnlyList<NpgsqlParameter> Parameters);
