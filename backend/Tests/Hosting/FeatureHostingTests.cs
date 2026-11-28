using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Infrastructure.Hosting;
using TemplateApp.Infrastructure.Http;
using Xunit;

namespace TemplateApp.Tests.Hosting;

public sealed class FeatureHostingTests
{
    [Fact]
    public async Task AFeatureInTheHostAssembly_RegistersItsServices_AndAnswersItsPublicAndInternalRoutes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddVersionedApi();
        builder.Services.AddFeatures(typeof(InvoicesFeature).Assembly);
        await using WebApplication app = builder.Build();
        app.MapFeatures();
        await app.StartAsync(ct);
        using HttpClient client = app.GetTestClient();

        Assert.Equal(["Invoices"], app.Services.GetRequiredService<HostFeatures>().Names);
        Assert.Equal("pública registrada", await client.GetStringAsync(new Uri("/api/v1/invoices", UriKind.Relative), ct));
        Assert.Equal("interna registrada", await client.GetStringAsync(new Uri("/internal/v1/invoices", UriKind.Relative), ct));
    }
}
