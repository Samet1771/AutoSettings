using AutoSettings.Core.Catalog;
using AutoSettings.Core.Model;
using AutoSettings.Core.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoSettings.Core.Config;

/// <summary>
/// Reads the structure of an <c>automations.yaml</c> file. Values are kept raw; type checking
/// against the catalog happens in <see cref="ConfigValidator"/>.
/// </summary>
public static class YamlConfigReader
{
    private static readonly string[] RootKeys = ["version", "automations", "profiles"];
    private static readonly string[] AutomationKeys = ["id", "name", "description", "enabled", "cooldown", "triggers", "conditions", "actions"];
    private static readonly string[] ProfileKeys = ["id", "name", "description", "priority", "actions"];

    /// <summary>Parses <paramref name="yaml"/>. Syntax errors are reported as issues, never thrown.</summary>
    public static (AutomationConfig Config, List<ConfigIssue> Issues) Read(string yaml)
    {
        var issues = new List<ConfigIssue>();
        var config = new AutomationConfig();

        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        }
        catch (YamlException ex)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error, "YAML syntax error: " + CleanMessage(ex.Message), ToLocation(ex.Start)));
            return (config, issues);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // For example duplicate keys in a mapping.
            issues.Add(new ConfigIssue(IssueSeverity.Error, "YAML error: " + ex.Message));
            return (config, issues);
        }

        if (root is null)
            return (config, issues);

        var tree = ToRaw(root);
        if (tree is null)
            return (config, issues);
        if (tree is not RawMap map)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error,
                "The file must be a map with 'version', 'automations' and 'profiles' keys.", ToLocation(root.Start)));
            return (config, issues);
        }

        foreach (var (key, value) in map)
        {
            var location = map.LocationOf(key);
            switch (key)
            {
                case "version":
                    if (ValueConverter.TryToInteger(value, out var version))
                        config.Version = (int)version;
                    else
                        issues.Add(new ConfigIssue(IssueSeverity.Error, "'version' must be a number.", location));
                    break;

                case "automations":
                    foreach (var (item, index) in ReadList(value, "automations", location, issues))
                        config.Automations.Add(ReadAutomation(item, index, issues));
                    break;

                case "profiles":
                    foreach (var (item, index) in ReadList(value, "profiles", location, issues))
                        config.Profiles.Add(ReadProfile(item, index, issues));
                    break;

                default:
                    issues.Add(UnknownKey(key, "at the top level", RootKeys, location));
                    break;
            }
        }

        return (config, issues);
    }

    /// <summary>
    /// Reads a list of triggers, conditions or actions from a raw value (a list of maps, a single map,
    /// or plain type names such as <c>- lock</c>).
    /// </summary>
    public static List<ComponentConfig> ReadComponents(object? value, ComponentKind kind, string context, SourceLocation? location, List<ConfigIssue> issues)
    {
        var result = new List<ComponentConfig>();
        var items = value switch
        {
            null => new List<object?>(),
            List<object?> list => list,
            _ => new List<object?> { value },
        };

        for (var i = 0; i < items.Count; i++)
        {
            var label = $"{context}, {KindLabel(kind)} {i + 1}";
            switch (items[i])
            {
                case RawMap map:
                {
                    if (!map.TryGetValue("type", out var typeValue) || ValueConverter.ToText(typeValue) is not { Length: > 0 } type)
                    {
                        issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: 'type' is missing.", map.Location ?? location));
                        continue;
                    }
                    var parameters = map.Where(p => p.Key != "type");
                    result.Add(new ComponentConfig(type.Trim(), parameters, map.Location));
                    break;
                }
                case string typeName when !string.IsNullOrWhiteSpace(typeName):
                    result.Add(new ComponentConfig(typeName.Trim(), location: location));
                    break;
                default:
                    issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: expected a map with a 'type'.", location));
                    break;
            }
        }
        return result;
    }

    private static Automation ReadAutomation(RawMap map, int index, List<ConfigIssue> issues)
    {
        var automation = new Automation { Location = map.Location };
        var label = map.TryGetValue("name", out var n) && n is string name ? $"Automation '{name}'" : $"Automation {index + 1}";

        foreach (var (key, value) in map)
        {
            var location = map.LocationOf(key);
            switch (key)
            {
                case "id":
                    automation.Id = ValueConverter.ToText(value)?.Trim() ?? "";
                    break;
                case "name":
                    automation.Name = ValueConverter.ToText(value);
                    break;
                case "description":
                    automation.Description = ValueConverter.ToText(value);
                    break;
                case "enabled":
                    if (ValueConverter.TryToBoolean(value, out var enabled))
                        automation.Enabled = enabled;
                    else
                        issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: 'enabled' must be true or false.", location));
                    break;
                case "cooldown":
                    if (value is null)
                        break;
                    if (ValueConverter.TryToDuration(value, out var cooldown))
                        automation.Cooldown = cooldown;
                    else
                        issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: 'cooldown' is not a duration (examples: 30s, 5m).", location));
                    break;
                case "triggers":
                case "trigger":
                    automation.Triggers.AddRange(ReadComponents(value, ComponentKind.Trigger, label, location, issues));
                    break;
                case "conditions":
                case "condition":
                    automation.Conditions.AddRange(ReadComponents(value, ComponentKind.Condition, label, location, issues));
                    break;
                case "actions":
                case "action":
                    automation.Actions.AddRange(ReadComponents(value, ComponentKind.Action, label, location, issues));
                    break;
                default:
                    issues.Add(UnknownKey(key, $"in {label}", AutomationKeys, location));
                    break;
            }
        }
        return automation;
    }

    private static Profile ReadProfile(RawMap map, int index, List<ConfigIssue> issues)
    {
        var profile = new Profile { Location = map.Location };
        var label = map.TryGetValue("name", out var n) && n is string name ? $"Profile '{name}'" : $"Profile {index + 1}";

        foreach (var (key, value) in map)
        {
            var location = map.LocationOf(key);
            switch (key)
            {
                case "id":
                    profile.Id = ValueConverter.ToText(value)?.Trim() ?? "";
                    break;
                case "name":
                    profile.Name = ValueConverter.ToText(value);
                    break;
                case "description":
                    profile.Description = ValueConverter.ToText(value);
                    break;
                case "priority":
                    if (ValueConverter.TryToInteger(value, out var priority))
                        profile.Priority = (int)priority;
                    else
                        issues.Add(new ConfigIssue(IssueSeverity.Error, $"{label}: 'priority' must be a whole number.", location));
                    break;
                case "actions":
                case "action":
                    profile.Actions.AddRange(ReadComponents(value, ComponentKind.Action, label, location, issues));
                    break;
                default:
                    issues.Add(UnknownKey(key, $"in {label}", ProfileKeys, location));
                    break;
            }
        }
        return profile;
    }

    private static IEnumerable<(RawMap Item, int Index)> ReadList(object? value, string key, SourceLocation? location, List<ConfigIssue> issues)
    {
        if (value is null)
            yield break;
        if (value is not List<object?> list)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"'{key}' must be a list (each item starts with '- ').", location));
            yield break;
        }
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is RawMap map)
                yield return (map, i);
            else
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"'{key}' item {i + 1} must be a map.", location));
        }
    }

    private static ConfigIssue UnknownKey(string key, string where, IEnumerable<string> known, SourceLocation? location) =>
        new(IssueSeverity.Warning, $"Unknown key '{key}' {where} is ignored.{Suggestions.DidYouMean(key, known)}", location);

    private static string KindLabel(ComponentKind kind) => kind switch
    {
        ComponentKind.Trigger => "trigger",
        ComponentKind.Condition => "condition",
        _ => "action",
    };

    /// <summary>Converts a YAML node into plain values: <see cref="RawMap"/>, <c>List&lt;object?&gt;</c>, <see cref="string"/> or null.</summary>
    internal static object? ToRaw(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                if (scalar.Style == ScalarStyle.Plain && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL")
                    return null;
                return scalar.Value ?? "";

            case YamlSequenceNode sequence:
                return sequence.Children.Select(ToRaw).ToList();

            case YamlMappingNode mapping:
            {
                var map = new RawMap(ToLocation(mapping.Start));
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    var key = keyNode is YamlScalarNode k ? k.Value ?? "" : keyNode.ToString();
                    map[key] = ToRaw(valueNode);
                    map.KeyLocations[key] = ToLocation(keyNode.Start);
                }
                return map;
            }

            default:
                return null;
        }
    }

    internal static SourceLocation ToLocation(Mark mark) => new((int)mark.Line, (int)mark.Column);

    private static string CleanMessage(string message)
    {
        // YamlDotNet messages look like "(Line: 3, Col: 5, Idx: 20) - (Line: 3, Col: 6, Idx: 21): While scanning ...".
        if (message.StartsWith("(Line", StringComparison.Ordinal))
        {
            var end = message.IndexOf("): ", StringComparison.Ordinal);
            if (end >= 0)
                return message[(end + 3)..];
        }
        return message;
    }
}
