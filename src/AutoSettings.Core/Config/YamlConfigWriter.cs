using System.Text;
using System.Text.RegularExpressions;
using AutoSettings.Core.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoSettings.Core.Config;

/// <summary>
/// Writes an <see cref="AutomationConfig"/> as YAML in the same style people write by hand
/// (indented lists, block scalars for scripts, quotes only where needed).
/// </summary>
/// <remarks>Comments are not preserved: the model does not store them.</remarks>
public static partial class YamlConfigWriter
{
    /// <summary>Serializes <paramref name="config"/>. <paramref name="header"/> lines are written as comments at the top.</summary>
    public static string Write(AutomationConfig config, string? header = null)
    {
        var root = new YamlMappingNode
        {
            { "version", Plain(config.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
        };

        if (config.Automations.Count > 0)
            root.Add("automations", new YamlSequenceNode(config.Automations.Select(ToNode)));
        else
            root.Add("automations", new YamlSequenceNode { Style = YamlDotNet.Core.Events.SequenceStyle.Flow });

        if (config.Profiles.Count > 0)
            root.Add("profiles", new YamlSequenceNode(config.Profiles.Select(ToNode)));

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(header))
        {
            foreach (var line in header.Replace("\r\n", "\n").Split('\n'))
                sb.Append("# ").Append(line).Append('\n');
            sb.Append('\n');
        }

        sb.Append(Emit(root));

        // Separate top-level list items with a blank line for readability.
        var text = sb.ToString().Replace("\r\n", "\n");
        text = TopLevelItem().Replace(text, "\n$1");
        return text.TrimEnd() + "\n";
    }

    /// <summary>Writes one automation as a YAML map (the editor's per-automation YAML view).</summary>
    public static string WriteAutomation(Automation automation) => Emit(ToNode(automation)).TrimEnd() + "\n";

    /// <summary>Writes one profile as a YAML map.</summary>
    public static string WriteProfile(Profile profile) => Emit(ToNode(profile)).TrimEnd() + "\n";

    private static string Emit(YamlNode root)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb) { NewLine = "\n" };
        var stream = new YamlStream(new YamlDocument(root));
        var settings = EmitterSettings.Default.WithIndentedSequences();
        stream.Save(new Emitter(writer, settings), assignAnchors: false);
        writer.Flush();
        return sb.ToString().Replace("\r\n", "\n");
    }

    private static YamlMappingNode ToNode(Automation automation)
    {
        var node = new YamlMappingNode { { "id", Text(automation.Id) } };
        if (!string.IsNullOrWhiteSpace(automation.Name)) node.Add("name", Text(automation.Name!));
        if (!string.IsNullOrWhiteSpace(automation.Description)) node.Add("description", Text(automation.Description!));
        if (!automation.Enabled) node.Add("enabled", Plain("false"));
        if (automation.Cooldown is { } cooldown) node.Add("cooldown", Plain(ValueConverter.FormatDuration(cooldown)));
        node.Add("triggers", new YamlSequenceNode(automation.Triggers.Select(ToNode)));
        if (automation.Conditions.Count > 0)
            node.Add("conditions", new YamlSequenceNode(automation.Conditions.Select(ToNode)));
        node.Add("actions", new YamlSequenceNode(automation.Actions.Select(ToNode)));
        return node;
    }

    private static YamlMappingNode ToNode(Profile profile)
    {
        var node = new YamlMappingNode { { "id", Text(profile.Id) } };
        if (!string.IsNullOrWhiteSpace(profile.Name)) node.Add("name", Text(profile.Name!));
        if (!string.IsNullOrWhiteSpace(profile.Description)) node.Add("description", Text(profile.Description!));
        if (profile.Priority != 0) node.Add("priority", Plain(profile.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        node.Add("actions", new YamlSequenceNode(profile.Actions.Select(ToNode)));
        return node;
    }

    /// <summary>Converts one component to a YAML node.</summary>
    public static YamlMappingNode ToNode(ComponentConfig component)
    {
        var node = new YamlMappingNode { { "type", Text(component.Type) } };
        foreach (var (key, value) in component.Parameters)
        {
            if (value is null)
                continue;
            node.Add(key, ValueNode(value));
        }
        return node;
    }

    private static YamlNode ValueNode(object value) => value switch
    {
        string s => Text(s),
        bool b => Plain(b ? "true" : "false"),
        TimeSpan t => Plain(ValueConverter.FormatDuration(t)),
        TimeOnly t => Quoted(ValueConverter.FormatTime(t)),
        IEnumerable<ComponentConfig> components => new YamlSequenceNode(components.Select(c => (YamlNode)ToNode(c))),
        IEnumerable<string> strings => StringList(strings.ToList()),
        RawMap map => new YamlMappingNode(map.Where(p => p.Value is not null).Select(p =>
            new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode(p.Key), ValueNode(p.Value!)))),
        IEnumerable<object?> items => new YamlSequenceNode(items.Where(i => i is not null).Select(i => ValueNode(i!))),
        IFormattable f => Plain(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)),
        _ => Text(value.ToString() ?? ""),
    };

    private static YamlNode StringList(List<string> items)
    {
        if (items.Count == 1)
            return Text(items[0]);
        var sequence = new YamlSequenceNode(items.Select(Text));
        if (items.All(i => i.Length < 40 && !NeedsQuotes(i)))
            sequence.Style = YamlDotNet.Core.Events.SequenceStyle.Flow;
        return sequence;
    }

    private static YamlScalarNode Plain(string value) => new(value) { Style = ScalarStyle.Plain };

    private static YamlScalarNode Quoted(string value) => new(value) { Style = ScalarStyle.DoubleQuoted };

    internal static YamlNode Text(string value)
    {
        if (value.Contains('\n'))
            return new YamlScalarNode(value.Replace("\r\n", "\n")) { Style = ScalarStyle.Literal };
        return NeedsQuotes(value) ? Quoted(value) : Plain(value);
    }

    /// <summary>Whether a string must be quoted so YAML does not read it as something else.</summary>
    internal static bool NeedsQuotes(string value)
    {
        if (value.Length == 0 || value != value.Trim())
            return true;
        if (LooksLikeOtherType().IsMatch(value))
            return true;
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0]))
            return true;
        return value.Contains(": ") || value.Contains(" #") || value.EndsWith(':') || value.Contains('\t');
    }

    [GeneratedRegex(@"^(true|false|yes|no|on|off|y|n|null|~|[-+]?(\d[\d_]*)?(\.\d+)?([eE][-+]?\d+)?|0x[0-9a-fA-F]+|0o[0-7]+|\d+:\d+(:\d+)?|[-+]?\.(inf|nan))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LooksLikeOtherType();

    [GeneratedRegex(@"\n(  - id: )")]
    private static partial Regex TopLevelItem();
}
