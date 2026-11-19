using Xunit;

namespace TemplateApp.Tests.Support;

internal static class Eventually
{
    public static async Task<T> Async<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        T value = await read();
        while (!done(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            value = await read();
        }

        return value;
    }
}
