using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TemplateApp.Features.Tasks.Contracts;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks;

public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks").RequireAuthorization();

        group.MapPost("", CreateAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetByIdAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateTaskRequest request,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await module.CreateAsync(user.GetCallerId(), request, ct);
        return ApiResults.ToResult(result, value => Results.Created($"/api/v1/tasks/{value.Id}", value));
    }

    private static async Task<IResult> ListAsync(
        TasksModule module,
        ClaimsPrincipal user,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await module.ListAsync(user.GetCallerId(), page, pageSize, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await module.GetByIdAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTaskRequest request,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await module.UpdateAsync(user.GetCallerId(), id, request, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        TasksModule module,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        ErrorOr<Success> result = await module.DeleteAsync(user.GetCallerId(), id, ct);
        return ApiResults.ToResult(result, _ => Results.NoContent());
    }
}
