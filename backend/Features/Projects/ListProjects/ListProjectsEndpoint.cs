using System.Security.Claims;
using ErrorOr;
using TemplateApp.Shared.Http;
using Wolverine;

namespace TemplateApp.Features.Projects.ListProjects;

internal static class ListProjectsEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) => projects.MapGet("", HandleAsync);

    private static async Task<IResult> HandleAsync(
        IMessageBus bus,
        ClaimsPrincipal user,
        string? cursor = null,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var command = new ListProjectsQuery(user.GetCallerId(), cursor, pageSize);
        var result = await bus.InvokeAsync<ErrorOr<ProjectListResponse>>(command, ct);
        return ApiResults.ToResult(result, value => Results.Ok(value));
    }
}
