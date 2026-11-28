namespace TemplateApp.Tests.Support.Rows;

internal sealed class OwnedRows
{
    private readonly IReadOnlyDictionary<string, long> _byTable;

    public OwnedRows(IReadOnlyDictionary<string, long> byTable) => _byTable = byTable;

    public Dictionary<string, long> NonZero => _byTable.Where(table => table.Value > 0).ToDictionary();

    public long this[string table] => _byTable.TryGetValue(table, out long rows)
        ? rows
        : throw new KeyNotFoundException($"No context of these hosts erases {table} with its owner. They erase: {string.Join(", ", _byTable.Keys)}.");
}
