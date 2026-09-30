using System.Globalization;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;
using AutoSettings.Core.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AutoSettings.Core.Plugins;

/// <summary>
/// Reads <c>plugin.yaml</c>. Structural problems (unknown keys, wrong value types) are reported as issues; the
/// meaning of the values is checked by <see cref="PluginManifestValidator"/>.
/// </summary>
public static class PluginManifestReader
{
    /// <summary>Keys allowed at the top of <c>plugin.yaml</c>.</summary>
    public static readonly string[] RootKeys =
        ["id", "name", "description", "version", "publisher", "homepage", "kind", "scope", "sdk", "min_app_version", "entry", "permissions", "update", "components"];

    /// <summary>Keys allowed in a component.</summary>
    public static readonly string[] ComponentKeys =
        ["type", "kind", "title", "description", "category", "runs_as", "revertible", "available_in", "event", "opposite", "example", "notes", "fields", "scripts", "interval", "timeout"];

    /// <summary>Keys allowed in a field.</summary>
    public static readonly string[] FieldKeys =
        ["name", "type", "description", "required", "default", "values", "min", "max", "example", "key", "placeholders"];

    /// <summary>Keys allowed in <c>scripts</c>.</summary>
    public static readonly string[] ScriptKeys = ["apply", "capture", "restore", "evaluate", "poll"];

    /// <summary>Field type names as written in <c>plugin.yaml</c>.</summary>
    public static readonly IReadOnlyDictionary<string, FieldType> FieldTypes = new Dictionary<string, FieldType>(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = FieldType.String,
        ["multiline"] = FieldType.Multiline,
        ["path"] = FieldType.Path,
        ["integer"] = FieldType.Integer,
        ["number"] = FieldType.Number,
        ["boolean"] = FieldType.Boolean,
        ["enum"] = FieldType.Enum,
        ["duration"] = FieldType.Duration,
        ["time"] = FieldType.Time,
        ["string_list"] = FieldType.StringList,
        ["app_list"] = FieldType.AppList,
        ["user_list"] = FieldType.UserList,
    };

    /// <summary>
    /// Reads and validates <paramref name="yaml"/> (see <see cref="PluginManifestValidator.Validate"/>). Use the
    /// manifest only when there are no errors.
    /// </summary>
    public static (PluginManifest Manifest, List<ConfigIssue> Issues) Load(string yaml, Updates.SemVersion? appVersion = null)
    {
        var (manifest, issues) = Read(yaml);
        if (!issues.Any(i => i.Severity == IssueSeverity.Error))
            issues.AddRange(PluginManifestValidator.Validate(manifest, appVersion));
        return (manifest, issues);
    }

    /// <summary>Parses <paramref name="yaml"/>. Problems are reported as issues, never thrown.</summary>
    public static (PluginManifest Manifest, List<ConfigIssue> Issues) Read(string yaml)
    {
        var reader = new Reader();
        var manifest = new PluginManifest();
        YamlNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        }
        catch (YamlException ex)
        {
            reader.Error("YAML syntax error: " + ex.Message, YamlConfigReader.ToLocation(ex.Start));
            return (manifest, reader.Issues);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            reader.Error("YAML error: " + ex.Message, null);
            return (manifest, reader.Issues);
        }

        if (root is not YamlMappingNode map)
        {
            reader.Error("plugin.yaml must be a map with keys such as 'id', 'name', 'version' and 'components'.", root is null ? null : YamlConfigReader.ToLocation(root.Start));
            return (manifest, reader.Issues);
        }

        foreach (var (key, value) in reader.Entries(map, RootKeys, "plugin.yaml"))
        {
            switch (key)
            {
                case "id": manifest.Id = reader.Text(value, key) ?? ""; break;
                case "name": manifest.Name = reader.Localized(value, key) ?? manifest.Name; break;
                case "description": manifest.Description = reader.Localized(value, key); break;
                case "version": manifest.Version = reader.Text(value, key) ?? ""; break;
                case "publisher": manifest.Publisher = reader.Text(value, key) ?? ""; break;
                case "homepage": manifest.Homepage = reader.Text(value, key); break;
                case "kind":
                    manifest.Kind = reader.Choice(value, key, new Dictionary<string, PluginKind> { ["dotnet"] = PluginKind.Dotnet, ["script"] = PluginKind.Script }) ?? manifest.Kind;
                    break;
                case "scope":
                    manifest.Scope = reader.Choice(value, key, Scopes) ?? manifest.Scope;
                    break;
                case "sdk": manifest.Sdk = reader.Text(value, key) ?? ""; break;
                case "min_app_version": manifest.MinAppVersion = reader.Text(value, key); break;
                case "entry": manifest.Entry = reader.Text(value, key); break;
                case "permissions":
                    foreach (var name in reader.TextList(value, key))
                    {
                        if (PluginPermissions.Parse(name) is { } permission)
                        {
                            if (!manifest.Permissions.Contains(permission))
                                manifest.Permissions.Add(permission);
                        }
                        else
                        {
                            reader.Error($"permissions: unknown permission '{name}'.{Suggestions.DidYouMean(name, PluginPermissions.Names)}", YamlConfigReader.ToLocation(value.Start));
                        }
                    }
                    break;
                case "update": manifest.Update = ReadUpdate(reader, value); break;
                case "components":
                    if (value is YamlSequenceNode list)
                    {
                        for (var i = 0; i < list.Children.Count; i++)
                        {
                            if (ReadComponent(reader, list.Children[i], $"components[{i + 1}]") is { } component)
                                manifest.Components.Add(component);
                        }
                    }
                    else
                    {
                        reader.Error("'components' must be a list.", YamlConfigReader.ToLocation(value.Start));
                    }
                    break;
            }
        }
        return (manifest, reader.Issues);
    }

    private static readonly Dictionary<string, ExecutionScope> Scopes = new() { ["user"] = ExecutionScope.User, ["machine"] = ExecutionScope.Machine };

    private static PluginUpdateSource? ReadUpdate(Reader reader, YamlNode node)
    {
        if (node is not YamlMappingNode map)
        {
            reader.Error("'update' must be a map with 'github' and optionally 'asset'.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }
        string? github = null;
        var asset = "*.aspkg";
        foreach (var (key, value) in reader.Entries(map, ["github", "asset"], "update"))
        {
            if (key == "github")
                github = reader.Text(value, "update.github");
            else
                asset = reader.Text(value, "update.asset") ?? asset;
        }
        if (github is null)
        {
            reader.Error("update: 'github' is required (owner/repository).", YamlConfigReader.ToLocation(node.Start));
            return null;
        }
        return new PluginUpdateSource(github, asset);
    }

    private static ManifestComponent? ReadComponent(Reader reader, YamlNode node, string label)
    {
        if (node is not YamlMappingNode map)
        {
            reader.Error($"{label}: must be a map with 'type', 'kind', 'title' and so on.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }
        var component = new ManifestComponent { Location = YamlConfigReader.ToLocation(node.Start) };
        var kinds = new Dictionary<string, ComponentKind> { ["action"] = ComponentKind.Action, ["condition"] = ComponentKind.Condition, ["trigger"] = ComponentKind.Trigger };
        var availability = new Dictionary<string, ScopeSupport> { ["user"] = ScopeSupport.User, ["machine"] = ScopeSupport.Machine, ["both"] = ScopeSupport.Both };
        var hasKind = false;
        foreach (var (key, value) in reader.Entries(map, ComponentKeys, label))
        {
            var where = $"{label}.{key}";
            switch (key)
            {
                case "type": component.Type = reader.Text(value, where) ?? ""; break;
                case "kind":
                    if (reader.Choice(value, where, kinds) is { } kind)
                    {
                        component.Kind = kind;
                        hasKind = true;
                    }
                    break;
                case "title": component.Title = reader.Localized(value, where) ?? component.Title; break;
                case "description": component.Description = reader.Localized(value, where) ?? component.Description; break;
                case "category": component.Category = reader.Text(value, where); break;
                case "runs_as": component.RunsAs = reader.Choice(value, where, Scopes) ?? component.RunsAs; break;
                case "revertible": component.Revertible = reader.Boolean(value, where) ?? false; break;
                case "available_in": component.AvailableIn = reader.Choice(value, where, availability) ?? component.AvailableIn; break;
                case "event": component.Event = reader.Text(value, where); break;
                case "opposite": component.Opposite = reader.Text(value, where); break;
                case "example": component.Example = reader.Text(value, where); break;
                case "notes": component.Notes = reader.Text(value, where); break;
                case "interval": component.Interval = reader.Duration(value, where); break;
                case "timeout": component.Timeout = reader.Duration(value, where); break;
                case "scripts":
                    if (value is YamlMappingNode scripts)
                    {
                        foreach (var (name, path) in reader.Entries(scripts, ScriptKeys, where))
                        {
                            var text = reader.Text(path, $"{where}.{name}");
                            switch (name)
                            {
                                case "apply": component.Scripts.Apply = text; break;
                                case "capture": component.Scripts.Capture = text; break;
                                case "restore": component.Scripts.Restore = text; break;
                                case "evaluate": component.Scripts.Evaluate = text; break;
                                case "poll": component.Scripts.Poll = text; break;
                            }
                        }
                    }
                    else
                    {
                        reader.Error($"{where}: must be a map, for example 'apply: set.ps1'.", YamlConfigReader.ToLocation(value.Start));
                    }
                    break;
                case "fields":
                    if (value is YamlSequenceNode fields)
                    {
                        for (var i = 0; i < fields.Children.Count; i++)
                        {
                            if (ReadField(reader, fields.Children[i], $"{where}[{i + 1}]") is { } field)
                                component.Fields.Add(field);
                        }
                    }
                    else
                    {
                        reader.Error($"{where}: must be a list.", YamlConfigReader.ToLocation(value.Start));
                    }
                    break;
            }
        }
        if (!hasKind)
            reader.Error($"{label}: 'kind' is required (action, condition or trigger).", component.Location);
        return component;
    }

    private static ManifestField? ReadField(Reader reader, YamlNode node, string label)
    {
        if (node is not YamlMappingNode map)
        {
            reader.Error($"{label}: must be a map with 'name', 'type' and 'description'.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }
        var field = new ManifestField();
        foreach (var (key, value) in reader.Entries(map, FieldKeys, label))
        {
            var where = $"{label}.{key}";
            switch (key)
            {
                case "name": field.Name = reader.Text(value, where) ?? ""; break;
                case "type":
                    var typeName = reader.Text(value, where);
                    if (typeName is not null && FieldTypes.TryGetValue(typeName, out var type))
                        field.Type = type;
                    else if (typeName is not null)
                        reader.Error($"{where}: unknown field type '{typeName}'.{Suggestions.DidYouMean(typeName, FieldTypes.Keys)}", YamlConfigReader.ToLocation(value.Start));
                    break;
                case "description": field.Description = reader.Localized(value, where) ?? field.Description; break;
                case "required": field.Required = reader.Boolean(value, where) ?? false; break;
                case "default": field.Default = value is YamlSequenceNode ? reader.TextList(value, where).ToList() : reader.Text(value, where); break;
                case "values": field.Values = reader.TextList(value, where).ToList(); break;
                case "min": field.Minimum = reader.Number(value, where); break;
                case "max": field.Maximum = reader.Number(value, where); break;
                case "example": field.Example = reader.Text(value, where); break;
                case "key": field.Key = reader.Boolean(value, where) ?? false; break;
                case "placeholders": field.Placeholders = reader.Boolean(value, where) ?? true; break;
            }
        }
        return field;
    }

    /// <summary>Small helpers that turn YAML nodes into values and collect issues.</summary>
    private sealed class Reader
    {
        public List<ConfigIssue> Issues { get; } = [];

        public void Error(string message, SourceLocation? location) =>
            Issues.Add(new ConfigIssue(IssueSeverity.Error, message, location));

        public IEnumerable<(string Key, YamlNode Value)> Entries(YamlMappingNode map, string[] allowed, string label)
        {
            foreach (var (keyNode, value) in map.Children)
            {
                var key = (keyNode as YamlScalarNode)?.Value ?? "";
                if (!allowed.Contains(key))
                {
                    Error($"{label}: unknown key '{key}'.{Suggestions.DidYouMean(key, allowed)}", YamlConfigReader.ToLocation(keyNode.Start));
                    continue;
                }
                yield return (key, value);
            }
        }

        public string? Text(YamlNode node, string where)
        {
            if (node is YamlScalarNode scalar)
                return scalar.Value;
            Error($"{where}: must be a single value.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }

        public IEnumerable<string> TextList(YamlNode node, string where) => node switch
        {
            YamlSequenceNode list => list.Children.Select(c => Text(c, where)).OfType<string>().ToList(),
            YamlScalarNode scalar when !string.IsNullOrEmpty(scalar.Value) => [scalar.Value!],
            YamlScalarNode => [],
            _ => ErrorList(node, where),
        };

        private List<string> ErrorList(YamlNode node, string where)
        {
            Error($"{where}: must be a list.", YamlConfigReader.ToLocation(node.Start));
            return [];
        }

        public LocalizedString? Localized(YamlNode node, string where)
        {
            if (node is YamlScalarNode scalar)
                return LocalizedString.Of(scalar.Value ?? "");
            if (node is YamlMappingNode map)
            {
                var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? english = null;
                foreach (var (keyNode, value) in map.Children)
                {
                    var language = ((keyNode as YamlScalarNode)?.Value ?? "").ToLowerInvariant();
                    var text = Text(value, $"{where}.{language}");
                    if (text is null)
                        continue;
                    if (language == "en")
                        english = text;
                    else
                        translations[language] = text;
                }
                if (english is null)
                {
                    Error($"{where}: the English text ('en') is required.", YamlConfigReader.ToLocation(node.Start));
                    return null;
                }
                return new LocalizedString(english, translations);
            }
            Error($"{where}: must be text, or a map of languages such as 'en: ...' and 'tr: ...'.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }

        public T? Choice<T>(YamlNode node, string where, IReadOnlyDictionary<string, T> choices) where T : struct
        {
            var text = Text(node, where);
            if (text is null)
                return null;
            foreach (var (name, value) in choices)
            {
                if (string.Equals(name, text.Trim(), StringComparison.OrdinalIgnoreCase))
                    return value;
            }
            Error($"{where}: must be one of {string.Join(", ", choices.Keys)} (got '{text}').", YamlConfigReader.ToLocation(node.Start));
            return null;
        }

        public bool? Boolean(YamlNode node, string where)
        {
            var text = Text(node, where);
            if (text is null)
                return null;
            if (bool.TryParse(text, out var value))
                return value;
            Error($"{where}: must be true or false.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }

        public double? Number(YamlNode node, string where)
        {
            var text = Text(node, where);
            if (text is null)
                return null;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
            Error($"{where}: must be a number.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }

        public TimeSpan? Duration(YamlNode node, string where)
        {
            var text = Text(node, where);
            if (text is null)
                return null;
            if (ValueConverter.TryParseDuration(text, out var value))
                return value;
            Error($"{where}: must be a duration such as 30s or 5m.", YamlConfigReader.ToLocation(node.Start));
            return null;
        }
    }
}
