using ErrorOr;

namespace TemplateApp.Shared.Http;

public static class HttpErrors
{
    public static readonly Error ValidationFailed = Error.Validation(
        code: "Validation.Failed",
        description: "One or more validation errors occurred.");

    public static readonly Error Unexpected = Error.Unexpected(
        code: "Http.Unexpected",
        description: "An unexpected error occurred.");
}
