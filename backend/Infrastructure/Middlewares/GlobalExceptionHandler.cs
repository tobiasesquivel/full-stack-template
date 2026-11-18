using Microsoft.AspNetCore.Diagnostics;
using TemplateApp.Infrastructure.Http;
using TemplateApp.Shared.Http;

namespace TemplateApp.Infrastructure.Middlewares;

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
