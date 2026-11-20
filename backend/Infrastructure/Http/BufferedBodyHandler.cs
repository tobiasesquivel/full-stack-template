namespace TemplateApp.Infrastructure.Http;

internal sealed class BufferedBodyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        try
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        return response;
    }
}
