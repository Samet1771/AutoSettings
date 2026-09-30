namespace AutoSettings.Core.Catalog;

/// <summary>
/// Plugin component types are named <c>publisher.plugin.name</c> (for example <c>acme.usb.connected</c>), so they
/// have at least two dots. Built-in types have at most one (<c>audio.volume</c>), so the two never clash.
/// </summary>
public static class PluginIds
{
    /// <summary>Whether <paramref name="type"/> is shaped like a plugin component type.</summary>
    public static bool IsPluginType(string? type) =>
        !string.IsNullOrEmpty(type) && type.Count(c => c == '.') >= 2 && !type.StartsWith('.') && !type.EndsWith('.');

    /// <summary>The plugin id of a plugin component type (everything before the last dot), or <c>null</c>.</summary>
    public static string? PluginOf(string? type) =>
        IsPluginType(type) ? type![..type!.LastIndexOf('.')] : null;
}
