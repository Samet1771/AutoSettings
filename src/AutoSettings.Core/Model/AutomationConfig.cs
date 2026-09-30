namespace AutoSettings.Core.Model;

/// <summary>
/// The contents of one <c>automations.yaml</c> file: automations plus the profiles they use.
/// </summary>
public sealed class AutomationConfig
{
    /// <summary>File format version. Currently always <see cref="Product.ConfigVersion"/>.</summary>
    public int Version { get; set; } = Product.ConfigVersion;

    /// <summary>The automations (rules) in this file.</summary>
    public List<Automation> Automations { get; set; } = [];

    /// <summary>The profiles (named sets of actions) in this file.</summary>
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>Finds a profile by id (case-insensitive).</summary>
    public Profile? FindProfile(string id) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds an automation by id (case-insensitive).</summary>
    public Automation? FindAutomation(string id) =>
        Automations.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A rule: <em>when</em> any trigger fires <em>and</em> all conditions hold, <em>then</em> run the actions in order.
/// </summary>
public sealed class Automation
{
    /// <summary>Unique, stable identifier (e.g. <c>gaming-mode</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Friendly name shown in the UI and the activity log.</summary>
    public string? Name { get; set; }

    /// <summary>Optional longer description.</summary>
    public string? Description { get; set; }

    /// <summary>Disabled automations are kept but never run.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum time between two runs. Triggers inside the cooldown are ignored.</summary>
    public TimeSpan? Cooldown { get; set; }

    /// <summary>Any of these starts the automation.</summary>
    public List<ComponentConfig> Triggers { get; set; } = [];

    /// <summary>All of these must be true for the actions to run.</summary>
    public List<ComponentConfig> Conditions { get; set; } = [];

    /// <summary>Run in order when the automation fires.</summary>
    public List<ComponentConfig> Actions { get; set; } = [];

    /// <summary>Where the automation starts in its YAML file, if loaded from one.</summary>
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// Plugins this automation uses that are not installed or are turned off (set by validation, not saved).
    /// Such an automation stays in the file but does not run until the plugins are available.
    /// </summary>
    public List<string> MissingPlugins { get; set; } = [];

    /// <summary>Whether the automation cannot run because a plugin it uses is missing.</summary>
    public bool IsBlocked => MissingPlugins.Count > 0;

    /// <summary><see cref="Name"/> if set, otherwise <see cref="Id"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;
}

/// <summary>
/// A named set of actions (for example "Gaming"). When applied by an automation it can be
/// reverted automatically, restoring every setting it changed.
/// </summary>
public sealed class Profile
{
    /// <summary>Unique identifier referenced by <c>profile.apply</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Friendly name.</summary>
    public string? Name { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When two active profiles change the same setting, the higher priority wins
    /// (ties go to the most recently applied profile).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>The actions that make up the profile.</summary>
    public List<ComponentConfig> Actions { get; set; } = [];

    /// <summary>Where the profile starts in its YAML file, if loaded from one.</summary>
    public SourceLocation? Location { get; set; }

    /// <summary><see cref="Name"/> if set, otherwise <see cref="Id"/>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name!;
}
