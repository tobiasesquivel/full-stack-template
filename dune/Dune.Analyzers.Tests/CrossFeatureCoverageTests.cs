using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Dune.Analyzers.Tests;

public sealed class CrossFeatureCoverageTests
{
    private const int FirstProbeLine = 6;

    private static readonly (string Path, string Source)[] Users =
    {
        ("backend/Features/Users/UserThing.cs", """
            namespace TemplateApp.Features.Users;
            public sealed record UserThing
            {
                public string Name { get; set; } = "";
                public UserThing? Next { get; set; }
            }
            """),
        ("backend/Features/Users/UserMarker.cs", """
            namespace TemplateApp.Features.Users;
            public sealed class UserMarker : System.Attribute { }
            """),
        ("backend/Features/Users/IUserInterior.cs", """
            namespace TemplateApp.Features.Users;
            public interface IUserInterior { }
            """),
        ("backend/Features/Users/UserBase.cs", """
            namespace TemplateApp.Features.Users;
            public abstract class UserBase { public static int Value => 1; }
            """),
        ("backend/Features/Users/UserText.cs", """
            namespace TemplateApp.Features.Users;
            public static class UserText
            {
                public static string Shout(this string s) => s.ToUpperInvariant();
            }
            """),
        ("backend/Features/Users/UserBlocks.cs", """
            namespace TemplateApp.Features.Users;
            public static class UserBlocks
            {
                extension(string s)
                {
                    public string Whisper() => s.ToLowerInvariant();
                    public int Weight => s.Length;
                    public static string Make() => "";
                }
                extension(System.Version)
                {
                    public static System.Version operator +(System.Version v, int n) => v;
                }
            }
            """),
        ("backend/Features/Users/UserPatterns.cs", """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            namespace TemplateApp.Features.Users;
            public static class UserPatterns
            {
                public static IEnumerator<int> GetEnumerator(this int n) { yield return n; }
                public static void Deconstruct(this Uri u, out string a, out string b) { a = u.Host; b = u.Scheme; }
                public static void Add(this List<int> list, string s) => list.Add(s.Length);
                public static TaskAwaiter GetAwaiter(this TimeSpan t) => System.Threading.Tasks.Task.Delay(t).GetAwaiter();
                public static IEnumerable<int> Select(this Version v, Func<int, int> f) { yield return f(v.Major); }
            }
            """),
        ("backend/Features/Users/Contracts/Inner/Secret.cs", """
            namespace TemplateApp.Features.Users.Contracts.Inner;
            public sealed class Secret { }
            """),
        ("backend/Features/Users/Contracts/UserInfo.cs", """
            namespace TemplateApp.Features.Users.Contracts;
            public sealed record UserInfo(string Name);
            """),
        ("backend/Features/Users/Contracts/UserInfoExtensions.cs", """
            namespace TemplateApp.Features.Users.Contracts;
            public static class UserInfoExtensions
            {
                public static string Label(this UserInfo info) => info.Name;
                extension(UserInfo info)
                {
                    public int Size => info.Name.Length;
                }
            }
            """),
        ("backend/Infrastructure/Bridge.cs", """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using TemplateApp.Features.Users;
            using TemplateApp.Features.Users.Contracts;
            namespace TemplateApp.Infrastructure;
            public static class Bridge
            {
                public static UserThing Get() => new();
                public static UserThing? Find() => null;
                public static UserThing[] All() => [];
                public static List<UserThing> List() => [];
                public static Task<UserThing> GetAsync() => Task.FromResult(new UserThing());
                public static (UserThing, int) Pair() => (new(), 1);
                public static void Take(UserThing t) { }
                public static void Each(Action<UserThing> a) { }
                public static T Echo<T>(T t) => t;
                public static void Run(Func<UserThing> f) { }
                public static UserInfo Info() => new("x");
                public static event Action<UserThing>? Changed;
                public static void Raise() => Changed?.Invoke(new());
            }
            """),
        ("backend/Features/Tasks/Own/OwnThing.cs", """
            namespace TemplateApp.Features.Tasks.Own;
            public sealed class OwnThing
            {
                public static OwnThing Make() => new();
                public string Name { get; set; } = "";
            }
            """),
    };

    public static readonly TheoryData<string, string> ForeignInteriorUses = new()
    {
        { "extension_method", "public object? M(string s) => s.Shout();" },
        { "extension_method_group", "public object? M(string s) { System.Func<string> f = s.Shout; return f; }" },
        { "extension_block_method", "public object? M(string s) => s.Whisper();" },
        { "extension_block_property", "public object? M(string s) => s.Weight;" },
        { "extension_block_static", "public object? M(string s) => string.Make();" },
        { "extension_block_operator", "public object? M(string s) => new System.Version() + 1;" },
        { "pattern_foreach_enumerator", "public object? M(string s) { foreach (var i in 3) { return i; } return null; }" },
        { "pattern_deconstruct", "public object? M(System.Uri u) { var (a, b) = u; return a + b; }" },
        { "pattern_collection_add", "public object? M(string s) => new System.Collections.Generic.List<int> { \"x\" };" },
        { "pattern_await", "public async System.Threading.Tasks.Task M() { await System.TimeSpan.Zero; }" },
        { "pattern_positional", "public object? M(System.Uri u) => u is (\"h\", _);" },
        { "pattern_foreach_deconstruct", "public object? M(System.Uri[] us) { foreach (var (a, b) in us) { return a; } return null; }" },
        { "pattern_linq_select", "public object? M(System.Version v) => from x in v select x;" },
        { "invocation_member", "public object? M() => Bridge.Get().Name;" },
        { "invocation_argument", "public object? M() => System.Convert.ToString(Bridge.Get());" },
        { "invocation_discarded", "public object? M() { Bridge.Get(); return null; }" },
        { "invocation_returned", "public object? M() => Bridge.Get();" },
        { "element_access", "public object? M() => Bridge.List()[0];" },
        { "await_result", "public async System.Threading.Tasks.Task<object?> M() => await Bridge.GetAsync();" },
        { "await_member", "public async System.Threading.Tasks.Task<object?> M() => (await Bridge.GetAsync()).Next;" },
        { "foreach_var", "public object? M() { foreach (var u in Bridge.All()) { return u; } return null; }" },
        { "foreach_object", "public object? M() { foreach (object u in Bridge.All()) { return u; } return null; }" },
        { "conditional_access", "public object? M() => Bridge.Find()?.Next;" },
        { "target_typed_new_initializer", "public object? M() { Bridge.Take(new() { Name = \"x\" }); return null; }" },
        { "target_typed_new", "public object? M() { Bridge.Take(new()); return null; }" },
        { "default_literal", "public object? M() { Bridge.Take(default!); return null; }" },
        { "with_expression", "public object? M() => Bridge.Get() with { };" },
        { "lambda_parameter_used", "public object? M() { Bridge.Each(u => u.Next = null); return null; }" },
        { "lambda_parameter_unused", "public object? M() { Bridge.Each(_ => { }); return null; }" },
        { "method_group_argument", "public object? M() { Bridge.Run(Bridge.Get); return null; }" },
        { "tuple_deconstruction", "public object? M() { var (u, n) = Bridge.Pair(); return n; }" },
        { "tuple_item", "public object? M() => Bridge.Pair().Item1;" },
        { "property_pattern", "public object? M() => Bridge.Get() is { Name: \"x\" };" },
        { "interpolation", "public object? M() => $\"{Bridge.Get()}\";" },
        { "generic_inference", "public object? M() => Bridge.Echo(Bridge.Get());" },
        { "conditional_operator", "public object? M(bool b) => b ? Bridge.Get() : null;" },
        { "coalesce", "public object? M() => Bridge.Find() ?? Bridge.Get();" },
        { "implicit_array", "public object? M() => new[] { Bridge.Get() };" },
        { "collection_expression", "public object? M() { System.Collections.Generic.List<object> l = [Bridge.Get()]; return l; }" },
        { "anonymous_type", "public object? M() => new { U = Bridge.Get() };" },
        { "event_subscription", "public object? M() { Bridge.Changed += _ => { }; return null; }" },
        { "typeof", "public object? M() => typeof(UserThing);" },
        { "nameof", "public object? M() => nameof(UserThing);" },
        { "attribute", "[UserMarker] public object? M() => null;" },
        { "generic_constraint", "public object? M<T>() where T : IUserInterior => null;" },
        { "type_pattern", "public object? M(object o) => o is UserThing;" },
        { "nested_base_class", "private sealed class Nested : UserBase { }" },
        { "static_member", "public object? M() => UserBase.Value;" },
        { "contracts_subfolder", "public TemplateApp.Features.Users.Contracts.Inner.Secret? M() => null;" },
    };

    [Theory]
    [MemberData(nameof(ForeignInteriorUses))]
    public async Task CrossFeatureRejectsEveryWayToReachForeignInterior(string construction, string member)
    {
        var diagnostics = await AnalyzeProbe(member);

        Assert.True(
            diagnostics.Any(diagnostic => diagnostic.Id == "DUNE0004"
                && diagnostic.Location.SourceTree!.FilePath.EndsWith("Probe.cs", StringComparison.Ordinal)
                && diagnostic.Location.GetLineSpan().StartLinePosition.Line >= FirstProbeLine),
            $"DUNE0004 no ve '{construction}'");
    }

    [Fact]
    public async Task CrossFeatureAcceptsContractsExtensionsAndOwnFeature()
    {
        var diagnostics = await AnalyzerHost.AnalyzeAsync(
            Users.Append(("backend/Features/Tasks/Probe.cs", """
                using System.Linq;
                using TemplateApp.Features.Tasks.Own;
                using TemplateApp.Features.Users.Contracts;
                using TemplateApp.Infrastructure;
                namespace TemplateApp.Features.Tasks;
                public sealed class Probe
                {
                    public object? M(int[] xs)
                    {
                        var info = Bridge.Info();
                        var label = info.Label() + Bridge.Info().Size + Bridge.Info().Name;
                        var own = OwnThing.Make().Name;
                        var ok = new OwnThing { Name = "x" };
                        var linq = xs.Where(x => x > 0).Select(x => x.ToString()).ToList();
                        return label + own + ok + linq + new[] { info } + string.Join(",", xs);
                    }
                }
                """)).ToList(),
            AnalyzerHost.Only<CrossFeatureAnalyzer>());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DUNE0004");
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeProbe(string member) =>
        AnalyzerHost.AnalyzeAsync(
            Users.Append(("backend/Features/Tasks/Probe.cs", $$"""
                using System.Linq;
                using TemplateApp.Features.Users;
                using TemplateApp.Infrastructure;
                namespace TemplateApp.Features.Tasks;
                public sealed class Probe
                {
                    {{member}}
                }
                """)).ToList(),
            AnalyzerHost.Only<CrossFeatureAnalyzer>());
}
