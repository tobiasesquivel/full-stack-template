using System.Globalization;
using System.Text.Json;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using FluentValidation;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

namespace TemplateApp.Infrastructure.Messaging;

internal sealed class MessagingExtension : IWolverineExtension
{
    private readonly MessagingOptions _options;
    private readonly string _connectionName;
    private readonly IReadOnlyList<IntegrationEventType> _published;
    private readonly IReadOnlyList<IntegrationEventSubscription> _subscriptions;
    private readonly IReadOnlyList<IntegrationCommandType> _commands;
    private readonly IReadOnlyList<SagaType> _sagas;
    private readonly IReadOnlyList<SagaSubscription> _sagaMessages;
    private readonly IReadOnlyList<MediatorHandlerType> _mediatorHandlers;

    public MessagingExtension(
        IOptions<MessagingOptions> options,
        IHostEnvironment environment,
        IEnumerable<IntegrationEventType> published,
        IEnumerable<IntegrationEventSubscription> subscriptions,
        IEnumerable<IntegrationCommandType> commands,
        IEnumerable<SagaType> sagas,
        IEnumerable<SagaSubscription> sagaMessages,
        IEnumerable<MediatorHandlerType> mediatorHandlers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        _options = options.Value;
        _connectionName = _options.Prefix.Length > 0 ? $"{environment.ApplicationName}/{_options.Prefix}" : environment.ApplicationName;
        _published = published.ToList();
        _subscriptions = subscriptions.ToList();
        _commands = commands.ToList();
        _sagas = sagas.ToList();
        _sagaMessages = sagaMessages.ToList();
        _mediatorHandlers = mediatorHandlers.ToList();
    }

    public void Configure(WolverineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
        options.Discovery.DisableConventionalDiscovery();
        options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;
        options.Durability.UseSyncRetryBlock = true;
        options.UseSystemTextJsonForSerialization(json => json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        QueueFailureRules.Apply(options.Policies);
        options.Policies.Add(new MessagingChainPolicy(_mediatorHandlers.Select(handler => handler.ClrType).ToHashSet()));

        RabbitMqTransportExpression rabbit = options.UseRabbitMq(_options.RabbitMqUri!)
            .AutoProvision()
            .DisableSystemRequestReplyQueueDeclaration()
            .ConfigureConnection(connection => connection.ClientProvidedName = _connectionName)
            .ConfigureChannelCreation(channel =>
            {
                channel.PublisherConfirmationsEnabled = true;
                channel.PublisherConfirmationTrackingEnabled = true;
            });
        foreach (IntegrationEventSubscription subscription in _subscriptions)
        {
            IntegrationEventNaming.Register(subscription.Event);
            options.RegisterMessageType(subscription.Event.ClrType, subscription.Event.Name);
            Listen(options, rabbit, subscription.Event, subscription.Queue).ProcessInline();
            options.AddMessageHandler(subscription.Event.ClrType, CreateHandler(subscription));
        }

        foreach (IntegrationEventType type in _published)
        {
            IntegrationEventNaming.Register(type);
            rabbit.DeclareExchange(Named(type.Exchange), exchange => exchange.ExchangeType = ExchangeType.Direct);
            options.PublishMessage(type.ClrType).ToRabbitRoutingKey(Named(type.Exchange), type.Name).SendInline();
        }

        foreach (MediatorHandlerType handler in _mediatorHandlers)
            IncludeMediatorHandler(options, handler.ClrType);

        if (_sagas.Count > 0)
            ConfigureSagas(options, rabbit);
    }

    private void ConfigureSagas(WolverineOptions options, RabbitMqTransportExpression rabbit)
    {
        foreach (SagaType saga in _sagas)
            options.Discovery.IncludeType(saga.ClrType);

        HashSet<Type> checkedMessages = [];
        foreach (SagaSubscription subscription in _sagaMessages)
        {
            IntegrationEnvelopeCheck.Expect(subscription.Message);
            checkedMessages.Add(subscription.Message.ClrType);
            Listen(options, rabbit, subscription.Message, subscription.Queue).UseDurableInbox();
        }

        options.Policies.AddMiddleware(typeof(IntegrationEnvelopeCheck), chain => checkedMessages.Contains(chain.MessageType));

        foreach (IntegrationCommandType command in _commands)
        {
            IntegrationEventType type = command.Command;
            IntegrationEventNaming.Register(type);
            rabbit.DeclareExchange(Named(type.Exchange), exchange => exchange.ExchangeType = ExchangeType.Direct);
            options.PublishMessage(type.ClrType)
                .ToRabbitRoutingKey(Named(type.Exchange), type.Name)
                .UseDurableOutbox()
                .CustomizeOutgoing(envelope => Stamp(envelope, type));
        }
    }

    private static void IncludeMediatorHandler(WolverineOptions options, Type handler)
    {
        options.Discovery.IncludeType(handler);
        Type message = handler.GetMethod("Handle")!.GetParameters()[0].ParameterType;
        IEnumerable<Type> hidden = handler.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Append(typeof(IValidator<>).MakeGenericType(message))
            .Append(typeof(IFailureAction<>).MakeGenericType(message))
            .Where(type => !type.IsVisible);
        foreach (Type dependency in hidden)
            options.CodeGeneration.AlwaysUseServiceLocationFor(dependency);
    }

    private RabbitMqListenerConfiguration Listen(WolverineOptions options, RabbitMqTransportExpression rabbit, IntegrationEventType message, string queue)
    {
        string named = Named(queue);
        IntegrationEventNaming.Register(message);
        rabbit.DeclareQueue(named, declared =>
        {
            declared.QueueType = QueueType.quorum;
            declared.Arguments["x-single-active-consumer"] = true;
        });
        rabbit.BindExchange(Named(message.Exchange), ExchangeType.Direct).ToQueue(named, message.Name);
        return options.ListenToRabbitQueue(named)
            .DeadLetterQueueing(new DeadLetterQueue($"{named}.dlq") { ConfigureQueue = dlq => dlq.QueueType = QueueType.quorum });
    }

    private static void Stamp(Envelope envelope, IntegrationEventType type)
    {
        envelope.Headers[MessageHeaders.MessageId] = envelope.Id.ToString("D");
        envelope.Headers[MessageHeaders.Version] = type.Version.ToString(CultureInfo.InvariantCulture);
        envelope.Headers[MessageHeaders.OccurredAt] = (envelope.SentAt == default ? DateTimeOffset.UtcNow : envelope.SentAt).ToString("O", CultureInfo.InvariantCulture);
    }

    private string Named(string identifier) => _options.Prefix.Length > 0 ? $"{_options.Prefix}-{identifier}" : identifier;

    private static Wolverine.Runtime.Handlers.MessageHandler CreateHandler(IntegrationEventSubscription subscription) =>
        (Wolverine.Runtime.Handlers.MessageHandler)Activator.CreateInstance(
            typeof(IntegrationEventHandler<>).MakeGenericType(subscription.Event.ClrType),
            subscription)!;
}
