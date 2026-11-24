using Microsoft.AspNetCore.TestHost;

namespace TemplateApp.Tests.Support.Hosts;

internal sealed class InMemoryRoutes(Func<HttpRequestMessage, TestServer> route) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpMessageInvoker server = new(route(request).CreateHandler());
        return await server.SendAsync(request, cancellationToken);
    }
}
