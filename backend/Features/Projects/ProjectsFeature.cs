using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Projects;

internal static class ProjectsFeature
{
    public static IServiceCollection AddProjectsFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<ProjectsDbContext>(ProjectsDbContext.Schema, adoptsLegacyTables: false);
        services.AddScoped<ProjectResponses>();
        services.AddScoped<CreateProject.CreateProject>();
        services.AddScoped<ListProjects.ListProjects>();
        services.AddScoped<GetProject.GetProject>();
        services.AddScoped<UpdateProject.UpdateProject>();
        return services;
    }

    public static RouteGroupBuilder MapProjectsEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder projects = api.MapGroup("/projects").RequireAuthorization();

        CreateProject.CreateProjectEndpoint.Map(projects);
        ListProjects.ListProjectsEndpoint.Map(projects);
        GetProject.GetProjectEndpoint.Map(projects);
        UpdateProject.UpdateProjectEndpoint.Map(projects);

        return api;
    }
}
