using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace TemplateApp.Features.Tasks;

public static partial class TasksEndpoints
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
}
