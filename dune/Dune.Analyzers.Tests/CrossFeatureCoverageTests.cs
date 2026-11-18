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
                    public static System.Version operator ++(System.Version v) => v;
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
                public static Dictionary<UserThing, int>.KeyCollection Keys() => new Dictionary<UserThing, int>().Keys;
                public static void Many(params UserThing[] items) { }
                public static void ManyList(params List<UserThing> items) { }
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
        ("backend/Features/Users/UserExtra.cs", """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using TemplateApp.Infrastructure;
            namespace TemplateApp.Features.Users;
            public static class UserExtra
            {
                public static async IAsyncEnumerator<int> GetAsyncEnumerator(this long n) { await Task.Yield(); yield return 1; }
                public static IEnumerable<int> Where(this Version v, Func<int, bool> f) { yield return v.Major; }
                public static int OrderBy(this Version v, Func<int, int> k) => 1;
                public static int Join(this Version v, Version w, Func<int, int> a, Func<int, int> b, Func<int, int, int> r) => 1;
                public static int GroupBy(this Version v, Func<int, int> k) => 1;
                public static IEnumerable<T> Select<T>(this DateTime d, Func<int, T> f) { yield return f(1); }
            }
            """),
        ("backend/Features/Users/UserCursor.cs", """
            namespace TemplateApp.Features.Users;
            public struct UserCursor
            {
                public int Current => 1;
                public bool MoveNext() => false;
            }
            """),
        ("backend/Features/Users/UserLimits.cs", """
            namespace TemplateApp.Features.Users;
            public class UserLimits
            {
                public const int Limit = 3;
                public static int Count { get; set; }
            }
            """),
        ("backend/Features/Users/UserHandler.cs", """
            namespace TemplateApp.Features.Users;
            [System.Runtime.CompilerServices.InterpolatedStringHandler]
            public ref struct UserHandler
            {
                public UserHandler(int literalLength, int formattedCount) { }
                public void AppendLiteral(string s) { }
                public void AppendFormatted<T>(T t) { }
            }
            """),
        ("backend/Infrastructure/GlobalUsings.cs", """
            global using static TemplateApp.Features.Users.UserLimits;
            global using UT = TemplateApp.Features.Users.UserThing;
            """),
        ("backend/Infrastructure/Bag.cs", """
            namespace TemplateApp.Infrastructure;
            public sealed class Bag : System.Collections.IEnumerable
            {
                public System.Collections.IEnumerator GetEnumerator() => System.Array.Empty<int>().GetEnumerator();
            }
            """),
        ("backend/Infrastructure/InfraLimits.cs", """
            namespace TemplateApp.Infrastructure;
            public class InfraLimits : TemplateApp.Features.Users.UserLimits { }
            """),
        ("backend/Infrastructure/Box.cs", """
            using TemplateApp.Features.Users;
            namespace TemplateApp.Infrastructure;
            public sealed class Box
            {
                public int Count => 1;
                public UserThing this[int i] => new();
                public UserThing Slice(int start, int length) => new();
            }
            """),
        ("backend/Infrastructure/Buf.cs", """
            using TemplateApp.Features.Users;
            namespace TemplateApp.Infrastructure;
            [System.Runtime.CompilerServices.InlineArray(2)]
            public struct Buf { private UserThing _e; }
            """),
        ("backend/Infrastructure/Bridge2.cs", """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using TemplateApp.Features.Users;
            namespace TemplateApp.Infrastructure;
            public static class Bridge2
            {
                public static TaskAwaiter<UserThing> GetAwaiter(this DateTimeOffset d) => System.Threading.Tasks.Task.FromResult(new UserThing()).GetAwaiter();
                public static IEnumerator<UserThing> GetEnumerator(this short n) { yield return new UserThing(); }
                public static UserCursor GetEnumerator(this byte n) => new();
                public static void Deconstruct(this Guid g, out UserThing a, out int b) { a = new(); b = 1; }
                public static Box Box() => new();
                public static Buf Buf() => new();
                public static void Log(UserHandler h) { }
                public static void Opt(UserThing? u = null) { }
                public static object Obj() => new UserThing();
                public static UserThing Thing() => new();
            }
            """),
        ("backend/Features/Users/UserOperators.cs", """
            using TemplateApp.Infrastructure;
            namespace TemplateApp.Features.Users;
            public static class UserOperators
            {
                extension(System.Guid)
                {
                    public static bool operator true(System.Guid g) => true;
                    public static bool operator false(System.Guid g) => false;
                    public static System.Guid operator &(System.Guid a, System.Guid b) => a;
                }
                extension(Thin t)
                {
                    public int Count => 1;
                }
            }
            """),
        ("backend/Infrastructure/Roster.cs", """
            using System.Collections;
            using System.Collections.Generic;
            using TemplateApp.Features.Users;
            namespace TemplateApp.Infrastructure;
            public sealed class Roster : IEnumerable<UserThing>
            {
                public static Roster Make() => new();
                public static InfraPair Pair() => new(new(), 1);
                public IEnumerator<UserThing> GetEnumerator() { yield return new UserThing(); }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """),
        ("backend/Infrastructure/Thin.cs", """
            namespace TemplateApp.Infrastructure;
            public sealed class Thin { public int this[int i] => i; }
            """),
        ("backend/Infrastructure/InfraPair.cs", """
            using TemplateApp.Features.Users;
            namespace TemplateApp.Infrastructure;
            public sealed record InfraPair(UserThing A, int B);
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
        { "collection_initializer_extension_add", "public object? M(string s) => new System.Collections.Generic.List<int> { \"x\" };" },
        { "pattern_await", "public async System.Threading.Tasks.Task M() { await System.TimeSpan.Zero; }" },
        { "pattern_positional", "public object? M(System.Uri u) => u is (\"h\", _);" },
        { "pattern_foreach_deconstruct", "public object? M(System.Uri[] us) { foreach (var (a, b) in us) { return a; } return null; }" },
        { "query_extension_select", "public object? M(System.Version v) => from x in v select x;" },
        { "extension_compound_assignment", "public object? M(System.Version v) { v += 1; return v; }" },
        { "extension_increment", "public object? M(System.Version v) { v++; return v; }" },
        { "pattern_nested_deconstruct", "public object? M(System.Uri u) { var ((a, b), n) = (u, 1); return a + n; }" },
        { "pattern_var_deconstruct", "public object? M(System.Uri u) => u is var (a, b) ? a : null;" },
        { "nested_type_of_generic", "public object? M() => Bridge.Keys().Count;" },
        { "params_array", "public object? M() { Bridge.Many(); return null; }" },
        { "params_collection", "public object? M() { Bridge.ManyList(); return null; }" },
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
        { "await_foreach_ext", "public async System.Threading.Tasks.Task M() { await foreach (var i in 5L) { } }" },
        { "query_where", "public object? M(System.Version v) => from x in v where x > 0 select x;" },
        { "query_orderby", "public object? M(System.Version v) => from x in v orderby x select x;" },
        { "query_join", "public object? M(System.Version v) => from a in v join b in v on a equals b select a;" },
        { "query_group", "public object? M(System.Version v) => from x in v group x by x;" },
        { "query_let", "public object? M(System.DateTime d) => from x in d let y = x + 1 select y;" },
        { "ext_property_pattern", "public object? M(string s) => s is { Weight: 3 };" },
        { "await_result_infra_awaiter", "public async System.Threading.Tasks.Task<object?> M() => await System.DateTimeOffset.Now;" },
        { "await_result_discarded", "public async System.Threading.Tasks.Task M() { await System.DateTimeOffset.Now; }" },
        { "foreach_element_infra_ext", "public object? M() { foreach (object o in (short)1) { return o; } return null; }" },
        { "foreach_foreign_enumerator", "public object? M() { foreach (var i in (byte)1) { return i; } return null; }" },
        { "deconstruct_infra_ext", "public object? M(System.Guid g) { (object a, int b) = g; return a; }" },
        { "positional_infra_ext", "public object? M(System.Guid g) => g is ({ }, 1);" },
        { "implicit_index", "public object? M() { object o = Bridge2.Box()[^1]; return o; }" },
        { "implicit_range", "public object? M() { object o = Bridge2.Box()[1..]; return o; }" },
        { "plain_indexer", "public object? M() { object o = Bridge2.Box()[0]; return o; }" },
        { "list_pattern", "public object? M() => Bridge2.Box() is [{ }];" },
        { "slice_pattern", "public object? M() => Bridge2.Box() is [_, .. { }];" },
        { "switch_list_arm", "public object? M() => Bridge2.Box() switch { [{ }] => 1, _ => 0 };" },
        { "inline_array", "public object? M() { var b = Bridge2.Buf(); object o = b[0]; return o; }" },
        { "inline_array_foreach", "public object? M() { var b = Bridge2.Buf(); foreach (object o in b) { return o; } return null; }" },
        { "interp_handler", "public object? M() { Bridge2.Log($\"a{1}\"); return null; }" },
        { "optional_default", "public object? M() { Bridge2.Opt(); return null; }" },
        { "using_static_property", "public object? M() => Count;" },
        { "using_static_const", "public object? M() => Limit;" },
        { "nameof_using_static", "public object? M() => nameof(Count);" },
        { "attr_arg_using_static_const", "[System.ComponentModel.DefaultValue(Limit)] public object? M() => null;" },
        { "param_default_using_static", "public object? M(int x = Limit) => x;" },
        { "switch_case_using_static", "public object? M(int x) { switch (x) { case Limit: return 1; } return 0; }" },
        { "const_pattern_using_static", "public object? M(int x) => x is Limit;" },
        { "inherited_const_attr", "[System.ComponentModel.DefaultValue(InfraLimits.Limit)] public object? M() => null;" },
        { "inherited_nameof", "public object? M() => nameof(InfraLimits.Count);" },
        { "inherited_static", "public object? M() => InfraLimits.Count;" },
        { "alias_typeof", "public object? M() => typeof(UT);" },
        { "alias_default", "public object? M() => default(UT);" },
        { "alias_nameof", "public object? M() => nameof(UT);" },
        { "cast_pattern_obj", "public object? M() => Bridge2.Obj() is UT;" },
        { "throw_switch", "public object? M(int x) => x switch { 1 => Bridge2.Thing(), _ => null };" },
        { "local_func_static_lambda", "public object? M() { System.Func<object> f = static () => Bridge2.Thing(); return f; }" },
        { "out_discard", "public object? M(System.Guid g) { g.Deconstruct(out _, out var n); return n; }" },
        { "tuple_literal_target", "public object? M() { (object, int) t = (Bridge2.Thing(), 1); return t; }" },
        { "goto_case_const", "public object? M(int x) { switch (x) { case 1: goto case Limit; case Limit: return 1; } return 0; }" },
        { "checked_ext_op", "public object? M(System.Version v) => checked(v + 1);" },
        { "is_not", "public object? M() => Bridge2.Obj() is not UT;" },
        { "extension_true_false_operators", "public object? M(System.Guid g) => g && g;" },
        { "extension_count_for_implicit_index", "public object? M(Thin t) => t[^1];" },
        { "foreach_element_of_infrastructure", "public object? M() { foreach (object o in Roster.Make()) { return o; } return null; }" },
        { "spread_element_of_infrastructure", "public object? M() { object[] a = [.. Roster.Make()]; return a; }" },
        { "deconstruct_infrastructure_record", "public object? M() { var (_, n) = Roster.Pair(); return n; }" },
        { "positional_infrastructure_record", "public object? M() => Roster.Pair() is ({ }, 1);" },
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
                    public async System.Threading.Tasks.Task<object?> FP(UserInfo info, UserInfo[] arr, System.Collections.Generic.List<UserInfo> list)
                    {
                        var a = await System.Threading.Tasks.Task.FromResult(info);
                        await System.Threading.Tasks.Task.Yield();
                        foreach (object o in list) { }
                        foreach (var o in arr) { }
                        object[] spread = [.. list, .. arr];
                        var lp = arr is [{ Name: "x" }, .. var rest] && arr[^1] is { Size: 1 };
                        var pos = info is UserInfo(var n) && n != "";
                        var (s1, s2) = (info, 1);
                        var w = info with { Name = "y" };
                        var eq = info == w;
                        var sw = arr switch { [] => 0, [_] => 1, _ => 2 };
                        object r = "abc"[1..] + "abc"[^1];
                        var q = from i in list where i.Name != "" orderby i.Name group i by i.Name into g select g.Key;
                        var q2 = from i in list join j in arr on i.Name equals j.Name let k = j.Size select k;
                        System.Collections.Generic.List<object> col = [info, .. list];
                        var nm = nameof(UserInfo.Name) + nameof(info.Name) + nameof(OwnThing.Make);
                        await foreach (var x in Gen()) { }
                        using var ms = new System.IO.MemoryStream();
                        lock (list) { }
                        return a.ToString() + lp + pos + s1 + s2 + eq + sw + r + q + q2 + col + nm + info.GetHashCode() + new Bag();
                    }
                    private static async System.Collections.Generic.IAsyncEnumerable<UserInfo> Gen() { await System.Threading.Tasks.Task.Yield(); yield return new("x"); }
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
