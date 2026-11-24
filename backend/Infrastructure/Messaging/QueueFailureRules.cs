using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Persistence.Sagas;

namespace TemplateApp.Infrastructure.Messaging;

internal static class QueueFailureRules
{
    private static readonly TimeSpan[] Cooldowns = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    private static readonly TimeSpan Pause = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] BugCooldowns = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];
    private static readonly TimeSpan[] ConcurrencyCooldowns = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1)];

    public static void Apply(IWithFailurePolicies chain)
    {
        chain.OnException<UnreadableMessageException>().MoveToErrorQueue();
        chain.OnException<ConsumerUnavailableException>()
            .Or<NpgsqlException>()
            .Or<TimeoutException>()
            .RetryWithCooldown(Cooldowns)
            .Then.RequeueIndefinitely().AndPauseProcessing(Pause);
        chain.OnException<SagaConcurrencyException>().Or<DbUpdateConcurrencyException>().RetryWithCooldown(ConcurrencyCooldowns);
        chain.OnException<Exception>().RetryWithCooldown(BugCooldowns);
    }
}
