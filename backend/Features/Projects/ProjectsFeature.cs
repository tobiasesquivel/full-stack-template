using TemplateApp.Features.Projects.Persistence;
using TemplateApp.Infrastructure.Persistence;

namespace TemplateApp.Features.Projects;

internal static class ProjectsFeature
{
    public static IServiceCollection AddProjectsFeature(this IServiceCollection services)
    {
        services.AddMigratedDbContext<ProjectsDbContext>(ProjectsDbContext.Schema, adoptsLegacyTables: false);
        return services;
    }
}
