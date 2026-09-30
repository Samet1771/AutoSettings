using AutoSettings.Core.Events;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Catalog;

/// <summary>Whether a descriptor describes a trigger, a condition or an action.</summary>
public enum ComponentKind
{
    /// <summary>Starts an automation.</summary>
    Trigger,
    /// <summary>Must be true for an automation to run.</summary>
    Condition,
    /// <summary>Does something.</summary>
    Action,
}

/// <summary>Where something runs.</summary>
public enum ExecutionScope
{
    /// <summary>In the signed-in user's agent, as that user (personal automations).</summary>
    User,
    /// <summary>In the Windows Service, as SYSTEM (machine automations, admin only).</summary>
    Machine,
}

/// <summary>Which kind of automation file a trigger or condition may be used in.</summary>
[Flags]
public enum ScopeSupport
{
    /// <summary>Personal automations (<c>%AppData%</c>).</summary>
    User = 1,
    /// <summary>Machine automations (<c>%ProgramData%</c>).</summary>
    Machine = 2,
    /// <summary>Both.</summary>
    Both = User | Machine,
}

/// <summary>
/// Metadata for one trigger, condition or action type. The same metadata drives validation,
/// the JSON Schema used by the YAML editor, the visual editor and the reference docs.
/// </summary>
public sealed record ComponentDescriptor
{
    /// <summary>Type id used in YAML, e.g. <c>app_focused</c> or <c>audio.volume</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Trigger, condition or action.</summary>
    public required ComponentKind Kind { get; init; }

    /// <summary>Group used by the editor and the docs, e.g. "Audio".</summary>
    public required string Category { get; init; }

    /// <summary>Short human title, e.g. "Set volume".</summary>
    public required string Title { get; init; }

    /// <summary>What it does, in plain language.</summary>
    public required string Description { get; init; }

    /// <summary>Parameters.</summary>
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = [];

    /// <summary>Which automation files it may appear in (triggers and conditions).</summary>
    public ScopeSupport AvailableIn { get; init; } = ScopeSupport.Both;

    /// <summary>Where an action executes. Actions that run as <see cref="ExecutionScope.Machine"/> are only allowed in machine automations.</summary>
    public ExecutionScope RunsAs { get; init; } = ExecutionScope.User;

    /// <summary>Optional override of <see cref="RunsAs"/> that depends on the parameters (e.g. registry hive).</summary>
    public Func<ComponentConfig, ExecutionScope>? RunsAsResolver { get; init; }

    /// <summary>Whether the action can capture the current value and restore it later (used by profiles).</summary>
    public bool Revertible { get; init; }

    /// <summary>
    /// Fields that identify <em>which</em> setting an action changes (e.g. the audio device).
    /// Two actions with the same type and key field values change the same setting.
    /// </summary>
    public IReadOnlyList<string> KeyFields { get; init; } = [];

    /// <summary>For triggers: the event kind it reacts to.</summary>
    public SystemEventKind? EventKind { get; init; }

    /// <summary>A YAML example of the entry (a list item, without the leading dash's indentation).</summary>
    public string? Example { get; init; }

    /// <summary>Extra details for the docs (limitations, requirements).</summary>
    public string? Notes { get; init; }

    /// <summary>Extra validation beyond per-field checks. Returns error messages.</summary>
    public Func<ComponentConfig, IEnumerable<string>>? Validate { get; init; }

    /// <summary>Where the component comes from: built in, or a plugin.</summary>
    public ComponentSource Source { get; init; } = ComponentSource.BuiltIn;

    /// <summary>Finds a field by name.</summary>
    public FieldDescriptor? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    /// <summary>Where this action executes for the given parameters.</summary>
    public ExecutionScope ResolveRunsAs(ComponentConfig config) => RunsAsResolver?.Invoke(config) ?? RunsAs;

    /// <summary>Identifies the setting changed by <paramref name="config"/> (see <see cref="KeyFields"/>).</summary>
    public string SettingKey(ComponentConfig config) =>
        KeyFields.Count == 0
            ? Type
            : Type + "|" + string.Join("|", KeyFields.Select(f => (config.GetString(f) ?? "").ToLowerInvariant()));
}

/// <summary>Where a component comes from.</summary>
/// <param name="PluginId">The plugin id, or <c>null</c> for built-in components.</param>
/// <param name="PluginVersion">The plugin version, or <c>null</c> for built-in components.</param>
/// <param name="PluginScope">Whether the plugin is installed for the machine or for one user; <c>null</c> for built-in components.</param>
public sealed record ComponentSource(string? PluginId, string? PluginVersion, ExecutionScope? PluginScope)
{
    /// <summary>Built into AutoSettings.</summary>
    public static ComponentSource BuiltIn { get; } = new(null, null, null);

    /// <summary>Whether the component is built in.</summary>
    public bool IsBuiltIn => PluginId is null;

    /// <summary>A component from a plugin.</summary>
    public static ComponentSource Plugin(string id, string version, ExecutionScope scope) => new(id, version, scope);
}
