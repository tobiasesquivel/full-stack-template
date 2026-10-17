using ErrorOr;
using FluentValidation.Results;

namespace TemplateApp.Shared.Http;

public static class ValidationErrors
{
    public static List<Error> From(ValidationResult result) =>
        result.Errors
            .Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage))
            .ToList();
}
