using ErrorOr;

namespace TemplateApp.Shared.Http;

public static class ApiResults
{
    public static IResult ToResult<T>(ErrorOr<T> result, Func<T, IResult> success) =>
        result.IsError ? Problem(result.Errors) : success(result.Value);

    public static IResult Problem(Error error) =>
        Results.Problem(statusCode: StatusFor(error.Type), title: error.Description, extensions: Code(error));

    private static IResult Problem(List<Error> errors)
    {
        if (!errors.All(e => e.Type == ErrorType.Validation))
            return Problem(errors[0]);

        Dictionary<string, string[]> fieldErrors = errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        return Results.ValidationProblem(fieldErrors, extensions: Code(HttpErrors.ValidationFailed));
    }

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static Dictionary<string, object?> Code(Error error) => new() { ["code"] = error.Code };
}
