using FluentValidation;

namespace TemplateApp.Shared.Http;

public static class KeysetPaginationRules
{
    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilderInitial<T, int> rule) =>
        rule
            .Must(KeysetCursor.IsValidPageSize)
            .WithMessage($"PageSize must be between 1 and {KeysetCursor.MaxPageSize}.");

    public static IRuleBuilderOptions<T, string?> ValidCursor<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Must(cursor => cursor is null || KeysetCursor.TryDecode(cursor, out _))
            .WithMessage("Cursor is invalid.");
}
