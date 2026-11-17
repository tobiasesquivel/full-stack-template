using Npgsql;

namespace TemplateApp.Tests.Support;

internal sealed record CapturedCommand(string Text, IReadOnlyList<NpgsqlParameter> Parameters);
