using ErrorOr;
using Microsoft.AspNetCore.Http;

namespace TemplateApp.Shared.Http;

public static class ApiResults
{
    public static IResult ToResult<T>(ErrorOr<T> result, Func<T, IResult> success)
    {
        if (!result.IsError)
            return success(result.Value);

        List<Error> errors = result.Errors;

        if (errors.All(e => e.Type == ErrorType.Validation))
        {
            Dictionary<string, string[]> validationErrors = errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(validationErrors);
        }

        Error first = errors[0];

        return first.Type switch
        {
            ErrorType.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: first.Description, extensions: new Dictionary<string, object?> { ["code"] = first.Code }),
            ErrorType.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: first.Description, extensions: new Dictionary<string, object?> { ["code"] = first.Code }),
            ErrorType.Unauthorized => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: first.Description, extensions: new Dictionary<string, object?> { ["code"] = first.Code }),
            ErrorType.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: first.Description, extensions: new Dictionary<string, object?> { ["code"] = first.Code }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: first.Description, extensions: new Dictionary<string, object?> { ["code"] = first.Code }),
        };
    }
}
