using System.Text.Json;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class MessagingExtension : IWolverineExtension
{
    private static readonly TimeSpan[] Cooldowns = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    private static readonly TimeSpan Pause = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] BugCooldowns = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];

    private readonly MessagingOptions _options;
    private readonly string _connectionName;
    private readonly IReadOnlyList<IntegrationEventType> _published;
    private readonly IReadOnlyList<IntegrationEventSubscription> _subscriptions;

    public MessagingExtension(
        IOptions<MessagingOptions> options,
        IHostEnvironment environment,
        IEnumerable<IntegrationEventType> published,
        IEnumerable<IntegrationEventSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        _options = options.Value;
        _connectionName = _options.Prefix.Length > 0 ? $"{environment.ApplicationName}/{_options.Prefix}" : environment.ApplicationName;
        _published = published.ToList();
        _subscriptions = subscriptions.ToList();
    }

    public void Configure(WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
        options.Discovery.DisableConventionalDiscovery();
        options.Durability.UseSyncRetryBlock = true;
        options.UseSystemTextJsonForSerialization(json => json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        options.Policies.OnException<UnreadableMessageException>().MoveToErrorQueue();
        options.Policies.OnException<ConsumerUnavailableException>()
            .Or<NpgsqlException>()
            .Or<TimeoutException>()
            .RetryWithCooldown(Cooldowns)
            .Then.RequeueIndefinitely().AndPauseProcessing(Pause);
        options.Policies.OnException<Exception>().RetryWithCooldown(BugCooldowns);

        RabbitMqTransportExpression rabbit = options.UseRabbitMq(_options.RabbitMqUri!)
            .AutoProvision()
            .DisableSystemRequestReplyQueueDeclaration()
            .ConfigureConnection(connection => connection.ClientProvidedName = _connectionName)
            .ConfigureChannelCreation(channel =>
            {
                channel.PublisherConfirmationsEnabled = true;
                channel.PublisherConfirmationTrackingEnabled = true;
            });
        foreach (IntegrationEventType type in _published)
        {
            IntegrationEventNaming.Register(type);
            rabbit.DeclareExchange(Named(type.Exchange), exchange => exchange.ExchangeType = ExchangeType.Direct);
            options.PublishMessage(type.ClrType).ToRabbitRoutingKey(Named(type.Exchange), type.Name).SendInline();
        }

        foreach (IntegrationEventSubscription subscription in _subscriptions)
        {
            string queue = Named(subscription.Queue);
            IntegrationEventNaming.Register(subscription.Event);
            options.RegisterMessageType(subscription.Event.ClrType, subscription.Event.Name);
            rabbit.DeclareQueue(queue, declared =>
            {
                declared.QueueType = QueueType.quorum;
                declared.Arguments["x-single-active-consumer"] = true;
            });
            rabbit.BindExchange(Named(subscription.Event.Exchange), ExchangeType.Direct).ToQueue(queue, subscription.Event.Name);
            options.ListenToRabbitQueue(queue)
                .DeadLetterQueueing(new DeadLetterQueue($"{queue}.dlq") { ConfigureQueue = dlq => dlq.QueueType = QueueType.quorum })
                .ProcessInline();
            options.AddMessageHandler(subscription.Event.ClrType, CreateHandler(subscription));
        }
    }

    private string Named(string identifier) => _options.Prefix.Length > 0 ? $"{_options.Prefix}-{identifier}" : identifier;

    private static Wolverine.Runtime.Handlers.MessageHandler CreateHandler(IntegrationEventSubscription subscription) =>
        (Wolverine.Runtime.Handlers.MessageHandler)Activator.CreateInstance(
            typeof(IntegrationEventHandler<>).MakeGenericType(subscription.Event.ClrType),
            subscription)!;
}
