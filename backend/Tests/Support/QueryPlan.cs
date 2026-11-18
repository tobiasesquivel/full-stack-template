using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace TemplateApp.Tests.Support;

internal sealed record QueryPlan(IReadOnlyList<QueryPlanNode> Nodes, double ExecutionMilliseconds)
{
    public static async Task<QueryPlan> ExplainAnalyzeAsync(string connectionString, CapturedCommand command)
    {
        await using NpgsqlConnection connection = await TestDatabases.OpenAsync(connectionString);
        await using NpgsqlCommand explain = new($"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {command.Text}", connection);
        foreach (NpgsqlParameter parameter in command.Parameters)
            explain.Parameters.Add(parameter.Clone());

        string json = (string)(await explain.ExecuteScalarAsync())!;
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement[0];

        List<QueryPlanNode> nodes = [];
        Collect(root.GetProperty("Plan"), nodes);
        return new QueryPlan(nodes, root.GetProperty("Execution Time").GetDouble());
    }

    public IEnumerable<QueryPlanNode> OfType(string nodeType) =>
        Nodes.Where(node => node.NodeType == nodeType);

    public override string ToString() =>
        string.Join(" -> ", Nodes.Select(node =>
            $"{node.NodeType}{(node.IndexName is null ? "" : $" using {node.IndexName}")}{(node.ScanDirection is null ? "" : $" {node.ScanDirection}")}{(node.IndexCondition is null ? "" : $" cond={node.IndexCondition}")} rows={node.ActualRows} buffers={node.SharedBlocks}"))
        + string.Create(CultureInfo.InvariantCulture, $" | {ExecutionMilliseconds:0.###} ms");

    private static void Collect(JsonElement plan, List<QueryPlanNode> nodes)
    {
        nodes.Add(new QueryPlanNode(
            plan.GetProperty("Node Type").GetString()!,
            Text(plan, "Index Name"),
            Text(plan, "Scan Direction"),
            Text(plan, "Index Cond"),
            plan.GetProperty("Actual Rows").GetDouble() * plan.GetProperty("Actual Loops").GetDouble(),
            plan.GetProperty("Shared Hit Blocks").GetInt64() + plan.GetProperty("Shared Read Blocks").GetInt64()));

        if (plan.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
                Collect(child, nodes);
        }
    }

    private static string? Text(JsonElement plan, string property) =>
        plan.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
}
