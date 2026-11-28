using System.Reflection;

namespace TemplateApp.Infrastructure.Hosting;

public sealed class HostFeatures
{
    private const string Suffix = "Feature";

    private HostFeatures(IReadOnlyList<IFeature> all) => All = all;

    internal IReadOnlyList<IFeature> All { get; }

    public IReadOnlyList<string> Names => All.Select(feature => feature.GetType().Name[..^Suffix.Length]).ToList();

    internal static HostFeatures In(Assembly host) =>
        new(host.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IFeature).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IFeature)Activator.CreateInstance(type)!)
            .ToList());
}
