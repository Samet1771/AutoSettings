using System.Reflection;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Editing;

/// <summary>A ready-made set of automations and profiles users can add with one click.</summary>
/// <param name="Id">Template id (file name without extension).</param>
/// <param name="Title">Short title.</param>
/// <param name="Description">What it does.</param>
/// <param name="Icon">A Segoe Fluent Icons / emoji hint for the gallery.</param>
/// <param name="Yaml">The template file.</param>
public sealed record AutomationTemplate(string Id, string Title, string Description, string Icon, string Yaml)
{
    /// <summary>Parses the template (templates are validated by the unit tests).</summary>
    public AutomationConfig Load() => ConfigLoader.Load(Yaml, ExecutionScope.User).Config;
}

/// <summary>The built-in template gallery.</summary>
public static class Templates
{
    private static readonly (string Id, string Title, string Description, string Icon)[] Known =
    [
        ("gaming", "Gaming mode", "High-performance power, no sleep and your headset while a game launcher runs. Everything is restored when it closes.", "🎮"),
        ("presentation", "Presentation mode", "Mute sounds and keep the screen on while PowerPoint is the active window.", "📽"),
        ("night", "Day and night", "Dark mode and a dimmer screen in the evening, light mode in the morning.", "🌙"),
        ("meeting", "Meetings", "Use the headset for Teams and Zoom calls and restore your devices afterwards.", "🎧"),
        ("battery-saver", "Battery saver", "Power saver plan, dimmer screen and Bluetooth off when the battery runs low.", "🔋"),
        ("focus", "Focus time", "Close chat apps and silence notifications while you work in your editor.", "🎯"),
    ];

    /// <summary>All templates, in gallery order.</summary>
    public static IReadOnlyList<AutomationTemplate> All { get; } = Load();

    /// <summary>Finds a template by id.</summary>
    public static AutomationTemplate? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<AutomationTemplate> Load()
    {
        var assembly = typeof(Templates).Assembly;
        var result = new List<AutomationTemplate>();
        foreach (var (id, title, description, icon) in Known)
        {
            using var stream = assembly.GetManifestResourceStream($"AutoSettings.Templates.{id}.yaml")
                ?? throw new InvalidOperationException($"Template '{id}' is missing from the assembly.");
            using var reader = new StreamReader(stream);
            result.Add(new AutomationTemplate(id, title, description, icon, reader.ReadToEnd()));
        }
        return result;
    }
}
