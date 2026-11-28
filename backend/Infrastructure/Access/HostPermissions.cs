using System.Reflection;

namespace TemplateApp.Infrastructure.Access;

public sealed class HostPermissions
{
    private const string Suffix = "Permissions";

    public HostPermissions(IReadOnlyList<FeaturePermissions> all) => All = all;

    public IReadOnlyList<FeaturePermissions> All { get; }

    internal static HostPermissions In(Assembly host) =>
        new(host.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IFeaturePermissions).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(Declare)
            .ToList());

    private static FeaturePermissions Declare(Type type)
    {
        if (type.Name.Length <= Suffix.Length || !type.Name.EndsWith(Suffix, StringComparison.Ordinal))
            throw new InvalidOperationException($"{type.FullName} declares permissions and is not named <Feature>{Suffix}.");

        IFeaturePermissions declaration = (IFeaturePermissions)Activator.CreateInstance(type)!;
        return new FeaturePermissions(type.Name[..^Suffix.Length], declaration.Grants);
    }
}
