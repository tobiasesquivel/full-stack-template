using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemplateApp.Features.Users.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Users;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/users").RequireAuthorization();

        group.MapGet("/me", GetMeAsync);
        group.MapPut("/me", UpdateMeAsync);
        group.MapDelete("/me", DeleteMeAsync);
        group.MapGet("/{authId}", GetByIdAsync);
        group.MapDelete("/{authId}", DeleteByIdAsync);

        return app;
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await module.GetMeAsync(user.GetCallerId(), ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    private static async Task<IResult> UpdateMeAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await module.UpdateMeAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    private static async Task<IResult> DeleteMeAsync(
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await module.DeleteByIdAsync(user.GetCallerId(), user.GetCallerId(), ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }

    private static async Task<IResult> GetByIdAsync(
        string authId,
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<UserResponse> result = await module.GetByIdAsync(user.GetCallerId(), authId, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    private static async Task<IResult> DeleteByIdAsync(
        string authId,
        ClaimsPrincipal user,
        UsersModule module,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await module.DeleteByIdAsync(user.GetCallerId(), authId, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
