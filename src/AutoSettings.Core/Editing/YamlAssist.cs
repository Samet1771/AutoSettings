using System.Text.RegularExpressions;
using AutoSettings.Core.Catalog;

namespace AutoSettings.Core.Editing;

/// <summary>What a YAML text (or snippet) represents.</summary>
public enum YamlDocumentKind
{
    /// <summary>A whole automations.yaml file.</summary>
    File,
    /// <summary>A single automation map.</summary>
    Automation,
    /// <summary>A single profile map.</summary>
    Profile,
}

/// <summary>What a completion inserts.</summary>
public enum CompletionKind
{
    /// <summary>A trigger, condition or action type.</summary>
    Type,
    /// <summary>A field or key name.</summary>
    Key,
    /// <summary>A value.</summary>
    Value,
}

/// <summary>A completion suggestion.</summary>
/// <param name="Label">Shown in the list.</param>
/// <param name="Description">Shown next to the list.</param>
/// <param name="Kind">What it is.</param>
/// <param name="InsertText">Text inserted when chosen.</param>
public sealed record CompletionItem(string Label, string Description, CompletionKind Kind, string InsertText);

/// <summary>Completion suggestions and the text range they replace.</summary>
/// <param name="ReplaceStart">Offset where the partially typed word starts.</param>
/// <param name="Items">Suggestions.</param>
public sealed record CompletionResult(int ReplaceStart, IReadOnlyList<CompletionItem> Items);

/// <summary>Help for the word under the mouse.</summary>
/// <param name="Title">Short title.</param>
/// <param name="Description">Explanation.</param>
public sealed record HoverInfo(string Title, string Description);

/// <summary>
/// Autocomplete and hover help for automation YAML, based on the component catalog and the indentation
/// structure of the text. Works on incomplete (invalid) YAML because it only looks at lines.
/// </summary>
public static partial class YamlAssist
{
    private static readonly (string Key, string Description)[] RootKeys =
    [
        ("version", "File format version. Always 1."),
        ("automations", "The rules: when a trigger fires and all conditions are true, run the actions."),
        ("profiles", "Named sets of actions that are applied and reverted together."),
    ];

    private static readonly (string Key, string Description)[] AutomationKeys =
    [
        ("id", "Unique, stable id (letters, digits, dashes)."),
        ("name", "Name shown in the app and the activity log."),
        ("description", "Notes for yourself."),
        ("enabled", "false keeps the automation but never runs it."),
        ("cooldown", "Ignore triggers for this long after a run, e.g. 30s or 5m."),
        ("triggers", "WHEN: any of these starts the automation."),
        ("conditions", "IF: all of these must be true."),
        ("actions", "THEN: run in order."),
    ];

    private static readonly (string Key, string Description)[] ProfileKeys =
    [
        ("id", "Unique id, used by profile.apply."),
        ("name", "Name shown in the app."),
        ("description", "Notes for yourself."),
        ("priority", "When active profiles change the same setting, the higher priority wins."),
        ("actions", "The settings this profile applies."),
    ];

    private sealed record Line(int Index, int Start, int Indent, bool Dash, int KeyColumn, string? Key, string Value, bool Blank);

    private enum MapKind { Root, Automation, Profile, Component, Unknown }

    private sealed record MapContext(MapKind Kind, ComponentKind? ComponentKind, string? ComponentType, HashSet<string> Keys);

    /// <summary>Suggestions for the caret at <paramref name="offset"/>.</summary>
    public static CompletionResult Complete(
        string text,
        int offset,
        ExecutionScope scope,
        YamlDocumentKind documentKind = YamlDocumentKind.File,
        IEnumerable<string>? knownProfiles = null,
        ComponentCatalog? catalog = null)
    {
        catalog ??= ComponentCatalog.Default;
        offset = Math.Clamp(offset, 0, text.Length);
        var lines = Parse(text);
        var current = lines.Last(l => l.Start <= offset);
        var prefix = text[current.Start..offset];

        // "key: partial value"
        var valueMatch = ValueBeingTyped().Match(prefix);
        if (valueMatch.Success)
        {
            var key = valueMatch.Groups["key"].Value;
            var typed = valueMatch.Groups["value"].Value;
            var context = ResolveMap(lines, current, documentKind, catalog, keyColumnOverride: null);
            var items = ValuesFor(key, context, scope, catalog, ProfileIds(lines, documentKind).Concat(knownProfiles ?? []));
            return new CompletionResult(offset - typed.Length, Filter(items, typed));
        }

        // "partial key"
        var keyMatch = KeyBeingTyped().Match(prefix);
        if (keyMatch.Success)
        {
            var typed = keyMatch.Groups["key"].Value;
            var keyColumn = prefix.Length - typed.Length;
            var context = ResolveMap(lines, current, documentKind, catalog, keyColumn);
            var items = KeysFor(context, catalog);
            return new CompletionResult(offset - typed.Length, Filter(items, typed));
        }

        return new CompletionResult(offset, []);
    }

    /// <summary>Help for the key or type under <paramref name="offset"/>, or null.</summary>
    public static HoverInfo? Hover(string text, int offset, YamlDocumentKind documentKind = YamlDocumentKind.File, ComponentCatalog? catalog = null)
    {
        catalog ??= ComponentCatalog.Default;
        if (text.Length == 0)
            return null;
        offset = Math.Clamp(offset, 0, text.Length - 1);
        var lines = Parse(text);
        var line = lines.Last(l => l.Start <= offset);
        if (line.Key is null)
            return null;

        var column = offset - line.Start;
        var context = ResolveMap(lines, line, documentKind, catalog, keyColumnOverride: null);
        var onKey = column >= line.KeyColumn && column <= line.KeyColumn + line.Key.Length;

        if (line.Key == "type" && context.ComponentKind is { } kind)
        {
            var descriptor = catalog.Find(kind, line.Value.Trim().Trim('"', '\''));
            if (!onKey && descriptor is not null)
                return new HoverInfo($"{descriptor.Title} ({descriptor.Type})", descriptor.Description);
            return new HoverInfo("type", $"The {kind.ToString().ToLowerInvariant()} type.");
        }
        if (!onKey)
            return null;

        if (context.Kind == MapKind.Component && context.ComponentKind is { } componentKind && context.ComponentType is { } type
            && catalog.Find(componentKind, type)?.Field(line.Key) is { } field)
            return new HoverInfo(field.Name, field.Description);

        var keys = context.Kind switch
        {
            MapKind.Root => RootKeys,
            MapKind.Automation => AutomationKeys,
            MapKind.Profile => ProfileKeys,
            _ => [],
        };
        return keys.Where(k => k.Key == line.Key).Select(k => new HoverInfo(k.Key, k.Description)).FirstOrDefault();
    }

    private static IEnumerable<CompletionItem> KeysFor(MapContext context, ComponentCatalog catalog)
    {
        switch (context.Kind)
        {
            case MapKind.Root:
                return Keys(RootKeys, context.Keys);
            case MapKind.Automation:
                return Keys(AutomationKeys, context.Keys);
            case MapKind.Profile:
                return Keys(ProfileKeys, context.Keys);
            case MapKind.Component when context.ComponentType is null:
                return context.Keys.Contains("type") ? [] : [new CompletionItem("type", "What kind of entry this is.", CompletionKind.Key, "type: ")];
            case MapKind.Component:
                var descriptor = catalog.Find(context.ComponentKind!.Value, context.ComponentType!);
                if (descriptor is null)
                    return [];
                return descriptor.Fields
                    .Where(f => !context.Keys.Contains(f.Name))
                    .Select(f => new CompletionItem(f.Name, (f.Required ? "(required) " : "") + f.Description, CompletionKind.Key, f.Name + ": "));
            default:
                return [];
        }
    }

    private static IEnumerable<CompletionItem> Keys((string Key, string Description)[] keys, HashSet<string> present) =>
        keys.Where(k => !present.Contains(k.Key))
            .Select(k => new CompletionItem(k.Key, k.Description, CompletionKind.Key, k.Key + ": "));

    private static IEnumerable<CompletionItem> ValuesFor(string key, MapContext context, ExecutionScope scope, ComponentCatalog catalog, IEnumerable<string> profileIds)
    {
        if (key == "type" && context.ComponentKind is { } kind)
        {
            return catalog.OfKind(kind)
                .Where(d => IsUsable(d, scope))
                .Select(d => new CompletionItem(d.Type, $"{d.Title} — {d.Description}", CompletionKind.Type, d.Type));
        }

        if (context.Kind == MapKind.Automation && key == "enabled")
            return Booleans();

        if (context.Kind != MapKind.Component || context.ComponentKind is not { } componentKind || context.ComponentType is not { } type)
            return [];
        var field = catalog.Find(componentKind, type)?.Field(key);
        if (field is null)
            return [];

        if (field.Name == "profile" && type is BuiltInActions.ProfileApply or BuiltInActions.ProfileRevert or "profile_active")
            return profileIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => new CompletionItem(id, "Profile", CompletionKind.Value, id));
        if (field.Type == FieldType.Boolean)
            return Booleans();
        if (field.AllowedValues is { Count: > 0 } allowed)
            return allowed.Select(v => new CompletionItem(v, field.Description, CompletionKind.Value, v));
        return [];
    }

    private static CompletionItem[] Booleans() =>
    [
        new("true", "Yes / on", CompletionKind.Value, "true"),
        new("false", "No / off", CompletionKind.Value, "false"),
    ];

    private static bool IsUsable(ComponentDescriptor descriptor, ExecutionScope scope)
    {
        if (descriptor.Kind == ComponentKind.Action)
            return scope == ExecutionScope.Machine || descriptor.RunsAs == ExecutionScope.User || descriptor.RunsAsResolver is not null;
        return descriptor.AvailableIn.HasFlag(scope == ExecutionScope.User ? ScopeSupport.User : ScopeSupport.Machine);
    }

    private static List<CompletionItem> Filter(IEnumerable<CompletionItem> items, string typed) =>
        items.Where(i => typed.Length == 0 || i.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

    private static IEnumerable<string> ProfileIds(List<Line> lines, YamlDocumentKind documentKind)
    {
        foreach (var line in lines.Where(l => l.Key == "id"))
        {
            var context = ResolveMap(lines, line, documentKind, ComponentCatalog.Default, keyColumnOverride: null);
            if (context.Kind == MapKind.Profile && line.Value.Trim().Length > 0)
                yield return line.Value.Trim().Trim('"', '\'');
        }
    }

    /// <summary>
    /// Works out which map the line belongs to, its sibling keys, and for components their kind and type.
    /// Rules: keys of one map share a key column; a "- key:" line starts a list-item map; the parent is the
    /// nearest line above the map whose key column is smaller.
    /// </summary>
    private static MapContext ResolveMap(List<Line> lines, Line line, YamlDocumentKind documentKind, ComponentCatalog catalog, int? keyColumnOverride)
    {
        var column = keyColumnOverride ?? line.KeyColumn;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? type = null;
        void Add(Line l)
        {
            if (l.Key is null)
                return;
            keys.Add(l.Key);
            if (l.Key == "type")
                type = l.Value.Trim().Trim('"', '\'');
        }

        Add(line);
        var itemStart = line.Dash && line.KeyColumn == column ? line : null;
        Line? parent = null;

        if (itemStart is null)
        {
            for (var i = line.Index - 1; i >= 0; i--)
            {
                var above = lines[i];
                if (above.Blank || above.KeyColumn > column)
                    continue;
                if (above.KeyColumn < column)
                {
                    parent = above;
                    break;
                }
                Add(above);
                if (above.Dash)
                {
                    itemStart = above;
                    break;
                }
            }
        }

        for (var i = line.Index + 1; i < lines.Count; i++)
        {
            var below = lines[i];
            if (below.Blank)
                continue;
            if (below.KeyColumn < column || below.Dash && below.KeyColumn <= column)
                break;
            if (below.KeyColumn == column)
                Add(below);
        }

        if (itemStart is not null)
        {
            // The list's key: the nearest non-item line above the item with a key column at or left of the dash.
            for (var i = itemStart.Index - 1; i >= 0; i--)
            {
                var above = lines[i];
                if (above.Blank || above.Key is null)
                    continue;
                if (above.KeyColumn < itemStart.Indent || above.KeyColumn == itemStart.Indent && !above.Dash)
                {
                    parent = above;
                    break;
                }
            }
        }

        if (parent?.Key is null)
        {
            var rootKind = documentKind switch
            {
                YamlDocumentKind.Automation => MapKind.Automation,
                YamlDocumentKind.Profile => MapKind.Profile,
                _ => MapKind.Root,
            };
            return new MapContext(column == 0 && itemStart is null ? rootKind : MapKind.Unknown, null, null, keys);
        }

        return parent.Key switch
        {
            "automations" when itemStart is not null => new MapContext(MapKind.Automation, null, null, keys),
            "profiles" when itemStart is not null => new MapContext(MapKind.Profile, null, null, keys),
            "triggers" or "trigger" => new MapContext(MapKind.Component, ComponentKind.Trigger, type, keys),
            "conditions" or "condition" => new MapContext(MapKind.Component, ComponentKind.Condition, type, keys),
            "actions" or "action" => new MapContext(MapKind.Component, ComponentKind.Action, type, keys),
            _ => new MapContext(MapKind.Unknown, null, null, keys),
        };
    }

    private static List<Line> Parse(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        var index = 0;
        while (true)
        {
            var end = text.IndexOf('\n', start);
            var raw = (end < 0 ? text[start..] : text[start..end]).TrimEnd('\r');
            lines.Add(ParseLine(index++, start, raw));
            if (end < 0)
                break;
            start = end + 1;
        }
        return lines;
    }

    private static Line ParseLine(int index, int start, string raw)
    {
        var indent = raw.Length - raw.TrimStart(' ').Length;
        var rest = raw[indent..];
        if (rest.Length == 0 || rest.StartsWith('#'))
            return new Line(index, start, indent, false, indent, null, "", Blank: true);

        var dash = rest == "-" || rest.StartsWith("- ", StringComparison.Ordinal);
        var keyColumn = indent;
        if (dash)
        {
            var afterDash = rest.Length > 1 ? rest[1..] : "";
            var spaces = afterDash.Length - afterDash.TrimStart(' ').Length;
            keyColumn = indent + 1 + spaces;
            rest = afterDash.TrimStart(' ');
        }

        var match = KeyValue().Match(rest);
        return match.Success
            ? new Line(index, start, indent, dash, keyColumn, match.Groups["key"].Value, match.Groups["value"].Value, false)
            : new Line(index, start, indent, dash, keyColumn, null, rest, false);
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z_][\w.]*)\s*:(?:\s+(?<value>.*))?$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"^\s*(?:-\s+)?(?<key>[A-Za-z_][\w.]*):\s+(?<value>[^\s#""'\[\{]*)$")]
    private static partial Regex ValueBeingTyped();

    [GeneratedRegex(@"^\s*(?:-\s+)?(?<key>[A-Za-z_]*)$")]
    private static partial Regex KeyBeingTyped();
}
