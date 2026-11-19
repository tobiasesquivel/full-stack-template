using System.Net;
using System.Text.Json;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Infrastructure.Messaging;
using TemplateApp.Shared.Domain;

namespace TemplateApp.Features.Tasks.ForwardTaskEvents;

internal sealed class TaskEventsClient
{
    private readonly HttpClient _http;
    private readonly OutboxEvents _events;

    public TaskEventsClient(HttpClient http, OutboxEvents events)
    {
        _http = http;
        _events = events;
    }

    public async Task SendAsync(IDomainEvent @event, EventContext context, CancellationToken ct)
    {
        OutboxEventType type = _events.For(@event.GetType());
        ForwardedTaskEventRequest body = new(
            type.Name,
            type.Version,
            context.OccurredAt,
            JsonSerializer.SerializeToElement(@event, type.ClrType, JsonSerializerOptions.Web));
        using HttpRequestMessage request = new(HttpMethod.Put, $"internal/v1/task-events/{context.MessageId:D}")
        {
            Content = JsonContent.Create(body),
        };
        using HttpResponseMessage response = await _http.SendRemoteAsync(request, ct, HttpStatusCode.NoContent);
    }
}
