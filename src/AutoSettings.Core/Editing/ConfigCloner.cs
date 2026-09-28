using AutoSettings.Core.Model;

namespace AutoSettings.Core.Editing;

/// <summary>Deep copies of configuration objects (for duplicate, undo and templates).</summary>
public static class ConfigCloner
{
    /// <summary>Copies a whole configuration.</summary>
    public static AutomationConfig Clone(AutomationConfig config) => new()
    {
        Version = config.Version,
        Automations = config.Automations.Select(Clone).ToList(),
        Profiles = config.Profiles.Select(Clone).ToList(),
    };

    /// <summary>Copies an automation.</summary>
    public static Automation Clone(Automation automation) => new()
    {
        Id = automation.Id,
        Name = automation.Name,
        Description = automation.Description,
        Enabled = automation.Enabled,
        Cooldown = automation.Cooldown,
        Triggers = automation.Triggers.Select(Clone).ToList(),
        Conditions = automation.Conditions.Select(Clone).ToList(),
        Actions = automation.Actions.Select(Clone).ToList(),
    };

    /// <summary>Copies a profile.</summary>
    public static Profile Clone(Profile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Description = profile.Description,
        Priority = profile.Priority,
        Actions = profile.Actions.Select(Clone).ToList(),
    };

    /// <summary>Copies a component, including nested condition lists.</summary>
    public static ComponentConfig Clone(ComponentConfig component) =>
        new(component.Type, component.Parameters.Select(p => new KeyValuePair<string, object?>(p.Key, CloneValue(p.Value))));

    private static object? CloneValue(object? value) => value switch
    {
        IEnumerable<ComponentConfig> components => components.Select(Clone).ToList(),
        List<string> strings => new List<string>(strings),
        Config.RawMap map => CloneMap(map),
        List<object?> items => items.Select(CloneValue).ToList(),
        _ => value,
    };

    private static Config.RawMap CloneMap(Config.RawMap map)
    {
        var copy = new Config.RawMap(map.Location);
        foreach (var (key, value) in map)
            copy[key] = CloneValue(value);
        return copy;
    }
}
