using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Hosting;

namespace TemplateApp.Tests.Hosting;

public sealed class InvoicesFeature : IFeature
{
    public const string Registered = "invoices";

    public void AddServices(IServiceCollection services) =>
        services.AddKeyedSingleton(Registered, "registrada");

    public void MapEndpoints(RouteGroupBuilder api) =>
        api.MapGet("/invoices", ([FromKeyedServices(Registered)] string state) => Results.Text("pública " + state));

    public void MapInternalEndpoints(RouteGroupBuilder internalApi) =>
        internalApi.MapGet("/invoices", ([FromKeyedServices(Registered)] string state) => Results.Text("interna " + state));
}
