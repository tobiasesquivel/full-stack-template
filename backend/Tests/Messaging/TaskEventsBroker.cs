using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TemplateApp.Tests.Support;

namespace TemplateApp.Tests.Messaging;

internal sealed class TaskEventsBroker : IAsyncDisposable
{
    public const string Queue = "projects.task-counters";
    public const string DeadLetterQueue = "projects.task-counters.dlq";
    public const string Probe = "tests.probe";
    private const string Exchange = "tasks.task-events";

    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly HttpClient _management;
    private readonly string _prefix;

    private TaskEventsBroker(IConnection connection, IChannel channel, HttpClient management, string prefix)
    {
        _connection = connection;
        _channel = channel;
        _management = management;
        _prefix = prefix;
    }

    public static async Task<TaskEventsBroker> ConnectAsync(RabbitMqFixture rabbit, string prefix)
    {
        ConnectionFactory factory = new() { Uri = rabbit.Uri, ClientProvidedName = $"tests/{prefix}" };
        IConnection connection = await factory.CreateConnectionAsync();
        IChannel channel = await connection.CreateChannelAsync();
        HttpClient management = new() { BaseAddress = rabbit.ManagementUri };
        management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(rabbit.Uri.UserInfo))));
        return new TaskEventsBroker(connection, channel, management, prefix);
    }

    public string Name(string identifier) => $"{_prefix}-{identifier}";

    public async Task PublishAsync(string type, Guid messageId, object payload, string version = "1")
    {
        BasicProperties properties = new()
        {
            MessageId = messageId.ToString(),
            Type = type,
            ContentType = "application/json",
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                ["templateapp-message-id"] = messageId.ToString("D"),
                ["templateapp-message-version"] = version,
                ["templateapp-occurred-at"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            },
        };
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonSerializerOptions.Web);
        await _channel.BasicPublishAsync(Name(Exchange), type, mandatory: true, properties, body);
    }

    public async Task BindProbeAsync()
    {
        await _channel.ExchangeDeclareAsync(Name(Exchange), ExchangeType.Direct, durable: true);
        await _channel.QueueDeclareAsync(Name(Probe), durable: true, exclusive: false, autoDelete: false);
        foreach (string type in new[] { "tasks.task-created", "tasks.task-completed", "tasks.task-reopened", "tasks.task-deleted" })
            await _channel.QueueBindAsync(Name(Probe), Name(Exchange), type);
    }

    public async Task<BasicGetResult?> TakeAsync(string queue) =>
        await _channel.BasicGetAsync(Name(queue), autoAck: true);

    public async Task<uint> CountAsync(string queue) =>
        await _channel.MessageCountAsync(Name(queue));

    public async Task<int> ConsumersAsync(string queue)
    {
        using JsonDocument body = JsonDocument.Parse(await _management.GetStringAsync($"api/queues/%2F/{Uri.EscapeDataString(Name(queue))}"));
        return body.RootElement.TryGetProperty("consumers", out JsonElement consumers) ? consumers.GetInt32() : 0;
    }

    public async Task<int> CloseConnectionsAsync(string applicationName)
    {
        int closedCount = 0;
        using JsonDocument connections = JsonDocument.Parse(await _management.GetStringAsync("api/connections"));
        foreach (JsonElement connection in connections.RootElement.EnumerateArray())
        {
            string name = connection.GetProperty("name").GetString()!;
            if (!connection.TryGetProperty("client_properties", out JsonElement client)
                || !client.TryGetProperty("connection_name", out JsonElement label)
                || label.GetString() != $"{applicationName}/{_prefix}")
                continue;

            using HttpResponseMessage closed = await _management.DeleteAsync($"api/connections/{Uri.EscapeDataString(name)}");
            closed.EnsureSuccessStatusCode();
            closedCount++;
        }

        return closedCount;
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        _management.Dispose();
    }
}
