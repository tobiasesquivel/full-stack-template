using ErrorOr;
using Microsoft.AspNetCore.WebUtilities;

namespace TemplateApp.Shared.Http;

public static class HttpErrors
{
    public static readonly Error ValidationFailed = Error.Validation(
        code: "Validation.Failed",
        description: "One or more validation errors occurred.");

    public static readonly Error Unexpected = Error.Unexpected(
        code: "Http.Unexpected",
        description: "An unexpected error occurred.");

    public static readonly Error IdempotencyKeyInvalid = Error.Custom(
        type: 400,
        code: "Idempotency.KeyInvalid",
        description: "The Idempotency-Key header must be a UUID.");

    public static readonly Error IdempotencyKeyReused = Error.Custom(
        type: 422,
        code: "Idempotency.KeyReused",
        description: "The Idempotency-Key was already used with a different request.");

    private static readonly Dictionary<int, string> StatusCodes = new()
    {
        [400] = "Http.BadRequest",
        [404] = "Http.NotFound",
        [405] = "Http.MethodNotAllowed",
        [415] = "Http.UnsupportedMediaType",
        [503] = "Http.ServiceUnavailable",
    };

    public static string CodeFor(int status)
    {
        if (StatusCodes.TryGetValue(status, out string? code))
            return code;

        string reason = ReasonPhrases.GetReasonPhrase(status).Replace(" ", string.Empty, StringComparison.Ordinal);
        return reason.Length > 0 ? $"Http.{reason}" : $"Http.Status{status}";
    }
}
