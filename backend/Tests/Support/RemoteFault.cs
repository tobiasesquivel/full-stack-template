namespace TemplateApp.Tests.Support;

public sealed class RemoteFault
{
    private readonly Func<HttpRequestMessage, bool> _matches;

    private RemoteFault(Func<HttpRequestMessage, bool> matches, bool slow)
    {
        _matches = matches;
        Slow = slow;
    }

    public bool Active { get; set; } = true;

    public bool Slow { get; }

    public int Calls { get; private set; }

    public static RemoteFault Unreachable(Func<HttpRequestMessage, bool> matches) => new(matches, slow: false);

    public static RemoteFault Silent(Func<HttpRequestMessage, bool> matches) => new(matches, slow: true);

    public static Func<HttpRequestMessage, bool> Host(string authority) =>
        request => request.RequestUri!.Host == authority;

    public static Func<HttpRequestMessage, bool> Path(string prefix) =>
        request => request.RequestUri!.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal);

    public HttpMessageHandler? Intercept(HttpRequestMessage request)
    {
        if (!Active || !_matches(request))
            return null;

        Calls++;
        return new FaultHandler(Slow);
    }

    private sealed class FaultHandler(bool slow) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!slow)
                throw new HttpRequestException("Connection refused (in-memory fault).");

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
