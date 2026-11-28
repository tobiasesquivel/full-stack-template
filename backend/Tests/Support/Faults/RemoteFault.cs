using TemplateApp.Tests.Support.Hosts;

namespace TemplateApp.Tests.Support.Faults;

public sealed class RemoteFault
{
    private readonly Func<HttpRequestMessage, bool> _matches;

    private readonly Kind _kind;

    private RemoteFault(Func<HttpRequestMessage, bool> matches, Kind kind)
    {
        _matches = matches;
        _kind = kind;
    }

    private enum Kind
    {
        Unreachable,
        Silent,
        MissingRoute,
        LostResponse,
    }

    public bool Active { get; set; } = true;

    public int Calls { get; private set; }

    public static RemoteFault Unreachable(Func<HttpRequestMessage, bool> matches) => new(matches, Kind.Unreachable);

    public static RemoteFault Silent(Func<HttpRequestMessage, bool> matches) => new(matches, Kind.Silent);

    public static RemoteFault MissingRoute(Func<HttpRequestMessage, bool> matches) => new(matches, Kind.MissingRoute);

    public static RemoteFault LostResponse(Func<HttpRequestMessage, bool> matches) => new(matches, Kind.LostResponse);

    public static Func<HttpRequestMessage, bool> Host(string authority) =>
        request => request.RequestUri!.Host == authority;

    public static Func<HttpRequestMessage, bool> Path(string prefix) =>
        request => request.RequestUri!.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal);

    public HttpMessageHandler? Intercept(HttpRequestMessage request, HttpMessageHandler server)
    {
        if (!Active || !_matches(request) || ApiPair.IsPermissionsDeclaration(request))
            return null;

        Calls++;
        return new FaultHandler(_kind, server);
    }

    private sealed class FaultHandler(Kind kind, HttpMessageHandler server) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            switch (kind)
            {
                case Kind.Silent:
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Unreachable.");
                case Kind.MissingRoute:
                    return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                    {
                        Content = new StringContent(
                            "{\"status\":404,\"code\":\"Http.NotFound\"}",
                            System.Text.Encoding.UTF8,
                            "application/problem+json"),
                    };
                case Kind.LostResponse:
                    using (HttpMessageInvoker delivered = new(server, disposeHandler: false))
                    using (HttpResponseMessage lost = await delivered.SendAsync(request, cancellationToken))
                        throw new HttpRequestException($"The connection dropped after the server answered {(int)lost.StatusCode} (in-memory fault).");
                default:
                    throw new HttpRequestException("Connection refused (in-memory fault).");
            }
        }
    }
}
