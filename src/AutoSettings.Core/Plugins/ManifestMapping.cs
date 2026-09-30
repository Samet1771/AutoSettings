using System.Text;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Events;

namespace AutoSettings.Core.Plugins;

/// <summary>Turns a validated <see cref="PluginManifest"/> into catalog entries.</summary>
public static class ManifestMapping
{
    /// <summary>The field every plugin trigger gets, like the built-in triggers: filter by user.</summary>
    public static readonly FieldDescriptor UserField = Fields.Users(
        "user",
        "Only fire for events about one of these users. Accepts a user name, DOMAIN\\name, a SID, or wildcards. Leave empty for any user.",
        example: "samet");

    /// <summary>
    /// The components the plugin adds to the catalog, marked with the plugin as their <see cref="ComponentDescriptor.Source"/>.
    /// Call it only for manifests without validation errors.
    /// </summary>
    public static PluginContribution ToContribution(PluginManifest manifest)
    {
        var source = ComponentSource.Plugin(manifest.Id, manifest.Version, manifest.Scope);
        return new PluginContribution(source, manifest.Components.Select(c => ToDescriptor(manifest, c) with { Source = source }).ToList());
    }

    /// <summary>The catalog entry for one component.</summary>
    public static ComponentDescriptor ToDescriptor(PluginManifest manifest, ManifestComponent component)
    {
        var fields = component.Fields.Select(ToFieldDescriptor).ToList();
        if (component.Kind == ComponentKind.Trigger)
            fields.Insert(0, UserField);

        var localized = new Dictionary<string, LocalizedText>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, title) in component.Title.Translations)
            localized[language] = new LocalizedText(title, component.Description.Translations.GetValueOrDefault(language));

        return new ComponentDescriptor
        {
            Type = component.Type,
            Kind = component.Kind,
            Category = component.Category ?? manifest.Name.English,
            Title = component.Title.English,
            Description = component.Description.English,
            Fields = fields,
            AvailableIn = component.AvailableIn,
            RunsAs = component.Kind == ComponentKind.Action ? component.RunsAs : ExecutionScope.User,
            Revertible = component.Revertible,
            KeyFields = component.Fields.Where(f => f.Key).Select(f => f.Name).ToList(),
            EventKind = component.Kind == ComponentKind.Trigger ? SystemEventKind.Plugin : null,
            PluginEvent = component.Kind == ComponentKind.Trigger ? component.Event : null,
            OppositeEvent = component.Opposite,
            Example = component.Example ?? DefaultExample(component),
            Notes = component.Notes,
            Localized = localized,
        };
    }

    /// <summary>The catalog field for a manifest field.</summary>
    public static FieldDescriptor ToFieldDescriptor(ManifestField field) =>
        new(field.Name, field.Type, field.Description.English)
        {
            Required = field.Required,
            Default = field.Default,
            AllowedValues = field.Values,
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            Example = field.Example,
            AllowPlaceholders = field.Placeholders ? null : false,
        };

    private static string DefaultExample(ManifestComponent component)
    {
        var sb = new StringBuilder("type: ").Append(component.Type);
        foreach (var field in component.Fields.Where(f => f.Required))
            sb.Append('\n').Append(field.Name).Append(": ").Append(field.Example ?? field.Values?.FirstOrDefault() ?? "...");
        return sb.ToString();
    }
}
