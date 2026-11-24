using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using TemplateApp.Shared.Http;

namespace TemplateApp.Infrastructure.Http;

internal sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validation)
            return false;

        await ApiResults.Problem(ValidationErrors.From(new ValidationResult(validation.Errors))).ExecuteAsync(httpContext);
        return true;
    }
}
