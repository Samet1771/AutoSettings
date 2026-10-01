using System.Globalization;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;
using YamlDotNet.RepresentationModel;

namespace AutoSettings.Core.Plugins;

/// <summary>Writes a <see cref="PluginManifest"/> as <c>plugin.yaml</c> (the packing tool uses it for .NET plugins).</summary>
public static class PluginManifestWriter
{
    /// <summary>The YAML text of <paramref name="manifest"/>.</summary>
    public static string Write(PluginManifest manifest)
    {
        var root = new YamlMappingNode();
        Add(root, "id", manifest.Id);
        Add(root, "name", manifest.Name);
        Add(root, "description", manifest.Description);
        Add(root, "version", manifest.Version);
        Add(root, "publisher", manifest.Publisher);
        Add(root, "homepage", manifest.Homepage);
        Add(root, "kind", manifest.Kind == PluginKind.Dotnet ? "dotnet" : "script");
        Add(root, "scope", manifest.Scope == ExecutionScope.Machine ? "machine" : "user");
        root.Add("sdk", new YamlScalarNode(manifest.Sdk) { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted });
        Add(root, "min_app_version", manifest.MinAppVersion);
        Add(root, "entry", manifest.Entry);
        if (manifest.Permissions.Count > 0)
            root.Add("permissions", Flow(manifest.Permissions.Select(PluginPermissions.Name)));
        if (manifest.Update is { } update)
        {
            var map = new YamlMappingNode();
            Add(map, "github", update.GitHub);
            if (update.Asset != "*.aspkg")
                Add(map, "asset", update.Asset);
            root.Add("update", map);
        }

        var components = new YamlSequenceNode();
        foreach (var component in manifest.Components)
            components.Add(Component(component));
        root.Add("components", components);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        var text = writer.ToString().Replace("\r\n", "\n");
        // YamlDotNet ends documents with "..."; plugin.yaml files do not need it.
        if (text.EndsWith("...\n", StringComparison.Ordinal))
            text = text[..^4];
        return text;
    }

    private static YamlMappingNode Component(ManifestComponent component)
    {
        var map = new YamlMappingNode();
        Add(map, "type", component.Type);
        Add(map, "kind", component.Kind.ToString().ToLowerInvariant());
        Add(map, "title", component.Title);
        Add(map, "description", component.Description);
        Add(map, "category", component.Category);
        if (component.Kind == ComponentKind.Action && component.RunsAs == ExecutionScope.Machine)
            Add(map, "runs_as", "machine");
        if (component.Revertible)
            Plain(map, "revertible", "true");
        if (component.Kind != ComponentKind.Action && component.AvailableIn != ScopeSupport.Both)
            Add(map, "available_in", component.AvailableIn == ScopeSupport.User ? "user" : "machine");
        Add(map, "event", component.Event);
        Add(map, "opposite", component.Opposite);
        if (component.Interval is { } interval)
            Add(map, "interval", ValueConverter.FormatDuration(interval));
        if (component.Timeout is { } timeout)
            Add(map, "timeout", ValueConverter.FormatDuration(timeout));
        Add(map, "example", component.Example);
        Add(map, "notes", component.Notes);
        if (component.Fields.Count > 0)
        {
            var fields = new YamlSequenceNode();
            foreach (var field in component.Fields)
                fields.Add(Field(field));
            map.Add("fields", fields);
        }
        return map;
    }

    private static YamlMappingNode Field(ManifestField field)
    {
        var map = new YamlMappingNode();
        Add(map, "name", field.Name);
        Add(map, "type", PluginManifestReader.FieldTypes.First(t => t.Value == field.Type).Key);
        Add(map, "description", field.Description);
        if (field.Required)
            Plain(map, "required", "true");
        switch (field.Default)
        {
            case null: break;
            case IEnumerable<string> list when field.Default is not string: map.Add("default", Flow(list)); break;
            default: map.Add("default", YamlConfigWriter.Text(ValueConverter.ToText(field.Default) ?? "")); break;
        }
        if (field.Values is { Count: > 0 } values)
            map.Add("values", Flow(values));
        if (field.Minimum is { } min)
            Plain(map, "min", min.ToString(CultureInfo.InvariantCulture));
        if (field.Maximum is { } max)
            Plain(map, "max", max.ToString(CultureInfo.InvariantCulture));
        Add(map, "example", field.Example);
        if (field.Key)
            Plain(map, "key", "true");
        if (!field.Placeholders)
            Plain(map, "placeholders", "false");
        return map;
    }

    private static void Add(YamlMappingNode map, string key, string? value)
    {
        if (value is null)
            return;
        // Keywords such as true/machine are plain; other text is quoted when YAML would misread it.
        map.Add(key, YamlConfigWriter.Text(value));
    }

    private static void Plain(YamlMappingNode map, string key, string value) =>
        map.Add(key, new YamlScalarNode(value) { Style = YamlDotNet.Core.ScalarStyle.Plain });

    private static void Add(YamlMappingNode map, string key, LocalizedString? text)
    {
        if (text is null)
            return;
        if (text.Translations.Count == 0)
        {
            Add(map, key, text.English);
            return;
        }
        var languages = new YamlMappingNode { { "en", YamlConfigWriter.Text(text.English) } };
        foreach (var (language, value) in text.Translations.OrderBy(t => t.Key, StringComparer.Ordinal))
            languages.Add(language, YamlConfigWriter.Text(value));
        map.Add(key, languages);
    }

    private static YamlSequenceNode Flow(IEnumerable<string> items) =>
        new(items.Select(YamlConfigWriter.Text)) { Style = YamlDotNet.Core.Events.SequenceStyle.Flow };
}
