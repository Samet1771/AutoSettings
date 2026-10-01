using System.Reflection;
using System.Text;
using AutoSettings.Sdk;

namespace AutoSettings.Core.Tests;

/// <summary>
/// Guards the plugin SDK's public API. Plugins are compiled against it, so removing or changing anything public breaks
/// them. If this test fails:
/// <list type="bullet">
/// <item>Only additions: copy the new text from the failure message into AutoSettings.Sdk.api.txt (raise the SDK minor version).</item>
/// <item>Removals or changes: that breaks plugins. Avoid it, or raise the SDK major version (see docs/plugins/versioning.md).</item>
/// </list>
/// </summary>
public class SdkApiTests
{
    public static string Surface()
    {
        var sb = new StringBuilder();
        foreach (var type in typeof(IPlugin).Assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var bases = new List<string>();
            if (type.BaseType is { } baseType && baseType != typeof(object))
                bases.Add(baseType.FullName!);
            bases.AddRange(type.GetInterfaces().Select(i => i.FullName!).Order(StringComparer.Ordinal));
            sb.Append(type.IsInterface ? "interface " : type.IsEnum ? "enum " : type.IsValueType ? "struct " : "class ")
              .Append(type.FullName)
              .Append(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")
              .Append('\n');
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var members = type.GetMembers(flags)
                .Where(m => m is not MethodInfo { IsSpecialName: true })
                .Select(m => m switch
                {
                    FieldInfo { IsLiteral: true } f => $"const {f}={f.GetRawConstantValue()}",
                    _ => $"{m.MemberType.ToString().ToLowerInvariant()} {m}",
                })
                .Order(StringComparer.Ordinal);
            foreach (var member in members)
                sb.Append("  ").Append(member).Append('\n');
        }
        // Generic arguments are printed with their assembly version ([[X, AutoSettings.Sdk, Version=...]]), which
        // changes with builds; keep just the type name.
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\[\[([^\[\],]+), [^\]]+\]\]", "[$1]");
    }

    [Fact]
    public void Public_api_matches_the_snapshot()
    {
        var root = TestSupport.FindRepositoryRoot();
        if (root is null)
            return;
        var path = Path.Combine(root, "tests", "AutoSettings.Core.Tests", "AutoSettings.Sdk.api.txt");
        var expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        var actual = Surface();

        Assert.True(expected == actual,
            "The public API of AutoSettings.Sdk changed. If that is intended (see the comment on SdkApiTests), " +
            $"replace {path} with:\n----- BEGIN API -----\n{actual}----- END API -----");
    }
}
