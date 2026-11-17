using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace TemplateApp.Tests.Support;

internal sealed class CapturedCommands : DbCommandInterceptor
{
    private readonly ConcurrentQueue<CapturedCommand> _commands = new();

    public void Reset() => _commands.Clear();

    public IReadOnlyList<CapturedCommand> Reading(string table) =>
        _commands.Where(command => command.Text.Contains($"FROM \"{table}\"", StringComparison.Ordinal)).ToList();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        NpgsqlParameter[] parameters = command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToArray();
        _commands.Enqueue(new CapturedCommand(command.CommandText, parameters));
        return ValueTask.FromResult(result);
    }
}
