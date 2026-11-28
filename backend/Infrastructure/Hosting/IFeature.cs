namespace TemplateApp.Infrastructure.Hosting;

public interface IFeature
{
    void AddServices(IServiceCollection services);

    void MapEndpoints(RouteGroupBuilder api);

    void MapInternalEndpoints(RouteGroupBuilder internalApi)
    {
    }
}
