using Microsoft.AspNetCore.Diagnostics;
using TemplateApp.Infrastructure.Http.Remote;
using TemplateApp.Shared.Http;

namespace TemplateApp.Infrastructure.Http;

public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await ApiResults.Problem(exception is RemoteServiceUnavailableException
            ? HttpErrors.ServiceUnavailable
            : HttpErrors.Unexpected).ExecuteAsync(httpContext);
        return true;
    }
}
