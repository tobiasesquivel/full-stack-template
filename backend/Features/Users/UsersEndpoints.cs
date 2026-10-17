using System.Security.Claims;
using ErrorOr;
using TemplateApp.Features.Auth.Contracts;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users;

public static class UsersEndpoints
{
    internal static async Task<IResult> GetMeAsync(
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await module.GetMeAsync(user.GetCallerId(), ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    internal static async Task<IResult> UpdateMeAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await module.UpdateMeAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    internal static async Task<IResult> DeleteMeAsync(
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await module.DeleteByIdAsync(user.GetCallerId(), user.GetCallerId(), ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }

    internal static async Task<IResult> GetByIdAsync(
        string authId,
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await module.GetByIdAsync(user.GetCallerId(), authId, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    internal static async Task<IResult> DeleteByIdAsync(
        string authId,
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await module.DeleteByIdAsync(user.GetCallerId(), authId, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
