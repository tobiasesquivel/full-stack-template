using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Messaging.Mediator;
using TemplateApp.Tests.Support;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Persistence;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TemplateApp.Tests.Messaging;

public sealed class MediatorTransactionTests : IClassFixture<ApiFixture>
{
    private const string ModeTag = "TransactionMiddlewareMode";

    private static readonly string[] MonolithFeatures = ["Auth", "Users", "Projects"];

    private readonly ApiFixture _fixture;

    public MediatorTransactionTests(ApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void EveryMonolithCommand_HasARegisteredHandler()
    {
        Type[] commands = typeof(MonolithHost).Assembly.GetTypes()
            .Where(type => MonolithFeatures.Any(feature => type.Namespace?.StartsWith($"TemplateApp.Features.{feature}.", StringComparison.Ordinal) == true))
            .Where(type => type.Name.EndsWith("Command", StringComparison.Ordinal) && !type.Name.EndsWith("IntegrationCommand", StringComparison.Ordinal))
            .ToArray();
        Type[] handled = _fixture.Services.GetServices<MediatorHandlerType>()
            .Select(handler => MessageOf(handler.ClrType))
            .ToArray();
        HandlerGraph graph = _fixture.Services.GetRequiredService<HandlerGraph>();

        Assert.NotEmpty(commands);
        Assert.Equal(commands.OrderBy(type => type.FullName), handled.OrderBy(type => type.FullName));
        Assert.All(commands, command => Assert.NotNull(graph.HandlerFor(command)));
    }

    [Fact]
    public void EachMonolithHandler_GetsTheTransactionOfItsRegistration()
    {
        HandlerGraph graph = _fixture.Services.GetRequiredService<HandlerGraph>();

        Assert.All(_fixture.Services.GetServices<MediatorHandlerType>(), registered =>
        {
            HandlerChain chain = ((MessageHandler)graph.HandlerFor(MessageOf(registered.ClrType))!).Chain!;
            bool opensScope = chain.Middleware.OfType<OpenCrossContextTransaction>().Any();
            bool stopsOnError = chain.Postprocessors.OfType<StopOnBusinessError>().Any();
            bool completes = chain.Postprocessors.OfType<CompleteCrossContextTransaction>().Any();
            TransactionMiddlewareMode? mode = chain.Tags.TryGetValue(ModeTag, out object? tagged) ? (TransactionMiddlewareMode)tagged : null;

            switch (registered.Transaction)
            {
                case MediatorTransaction.Atomic:
                    Assert.True(opensScope && stopsOnError && completes, $"{chain.MessageType.Name} is atomic");
                    Assert.Equal(TransactionMiddlewareMode.Lightweight, mode);
                    break;
                case MediatorTransaction.Eager:
                    Assert.False(opensScope || stopsOnError || completes, $"{chain.MessageType.Name} is Eager");
                    Assert.Equal(TransactionMiddlewareMode.Eager, mode);
                    break;
                default:
                    Assert.False(opensScope || stopsOnError || completes, $"{chain.MessageType.Name} has no transaction");
                    Assert.Equal(TransactionMiddlewareMode.Lightweight, mode);
                    break;
            }
        });
    }

    [Fact]
    public void AMonolithCommand_IsNeverQueuedDurably()
    {
        IWolverineRuntime runtime = _fixture.Services.GetRequiredService<IWolverineRuntime>();

        Assert.All(_fixture.Services.GetServices<MediatorHandlerType>(), registered =>
        {
            Endpoint queue = runtime.Endpoints.EndpointFor(new Uri($"local://{MessageOf(registered.ClrType).FullName!.ToLowerInvariant()}"))!;
            Assert.Equal(EndpointMode.BufferedInMemory, queue.Mode);
        });
    }

    [Fact]
    public void TheTasksHost_LeavesTheTransactionToItsSlices()
    {
        HandlerGraph graph = _fixture.TasksServices.GetRequiredService<HandlerGraph>();

        Assert.All(_fixture.TasksServices.GetServices<MediatorHandlerType>(), registered =>
        {
            HandlerChain chain = ((MessageHandler)graph.HandlerFor(MessageOf(registered.ClrType))!).Chain!;
            Assert.Equal(MediatorTransaction.None, registered.Transaction);
            Assert.Empty(chain.Middleware.OfType<OpenCrossContextTransaction>());
            Assert.False(chain.Tags.ContainsKey(ModeTag));
        });
    }

    private static Type MessageOf(Type handler) => handler.GetMethod("Handle")!.GetParameters()[0].ParameterType;
}
