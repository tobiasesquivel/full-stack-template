using Asp.Versioning;

namespace TemplateApp.Infrastructure.Http;

public static class VersionedApi
{
    public static IServiceCollection AddVersionedApi(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
                options.ReportApiVersions = true;
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            })
            .AddOpenApi();
        return services;
    }

    public static RouteGroupBuilder MapVersionedApi(this IEndpointRouteBuilder app) =>
        app.NewVersionedApi()
            .MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(1.0);
}
