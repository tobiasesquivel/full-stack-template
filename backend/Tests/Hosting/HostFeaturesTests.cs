using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TemplateApp.Hosts.Monolith;
using TemplateApp.Infrastructure.Hosting;
using TemplateApp.Tests.Support.Hosts;
using Xunit;

namespace TemplateApp.Tests.Hosting;

public sealed class HostFeaturesTests : IClassFixture<ApiFixture>
{
    private const string FeaturesNamespace = "TemplateApp.Features.";

    private readonly ApiFixture _fixture;

    public HostFeaturesTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public void TheMonolith_StartsEveryFeatureItCompiles_InOrderOfName()
    {
        IReadOnlyList<string> started = _fixture.Services.GetRequiredService<HostFeatures>().Names;

        Assert.Equal(CompiledFeatures(typeof(MonolithHost).Assembly), started);
        Assert.DoesNotContain("Tasks", started);
    }

    [Fact]
    public void TheTasksHost_StartsOnlyTasksAndHealth() =>
        Assert.Equal(["Health", "Tasks"], _fixture.TasksServices.GetRequiredService<HostFeatures>().Names);

    private static List<string> CompiledFeatures(Assembly host) =>
        host.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith(FeaturesNamespace, StringComparison.Ordinal))
            .Select(ns => ns[FeaturesNamespace.Length..].Split('.')[0])
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
}
