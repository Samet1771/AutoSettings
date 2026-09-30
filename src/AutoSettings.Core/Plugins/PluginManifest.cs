using AutoSettings.Core.Catalog;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Plugins;

/// <summary>How a plugin is implemented.</summary>
public enum PluginKind
{
    /// <summary>A .NET assembly built against AutoSettings.Sdk.</summary>
    Dotnet,
    /// <summary>PowerShell scripts described by the manifest; nothing to compile.</summary>
    Script,
}

/// <summary>Text with translations: English plus optional other languages.</summary>
/// <param name="English">The English text (always present).</param>
/// <param name="Translations">Other languages by two-letter code, for example <c>tr</c>.</param>
public sealed record LocalizedString(string English, IReadOnlyDictionary<string, string> Translations)
{
    /// <summary>English only.</summary>
    public static LocalizedString Of(string english) => new(english, new Dictionary<string, string>());

    /// <inheritdoc />
    public override string ToString() => English;
}

/// <summary>The contents of a plugin's <c>plugin.yaml</c>.</summary>
public sealed class PluginManifest
{
    /// <summary>Plugin id, <c>publisher.name</c>, for example <c>acme.usb</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name.</summary>
    public LocalizedString Name { get; set; } = LocalizedString.Of("");

    /// <summary>What the plugin does.</summary>
    public LocalizedString? Description { get; set; }

    /// <summary>Plugin version (semantic versioning), for example <c>1.2.0</c>.</summary>
    public string Version { get; set; } = "";

    /// <summary>Who made it.</summary>
    public string Publisher { get; set; } = "";

    /// <summary>A web page about the plugin.</summary>
    public string? Homepage { get; set; }

    /// <summary>.NET or script plugin.</summary>
    public PluginKind Kind { get; set; }

    /// <summary>
    /// <see cref="ExecutionScope.User"/>: installed by and for one user, runs as that user.
    /// <see cref="ExecutionScope.Machine"/>: installed by an administrator for everyone; its machine components run as SYSTEM.
    /// </summary>
    public ExecutionScope Scope { get; set; } = ExecutionScope.User;

    /// <summary>The SDK version it was built for, for example <c>1.0</c>.</summary>
    public string Sdk { get; set; } = "";

    /// <summary>The oldest AutoSettings version it works with.</summary>
    public string? MinAppVersion { get; set; }

    /// <summary>For .NET plugins: the plugin assembly, relative to the plugin folder.</summary>
    public string? Entry { get; set; }

    /// <summary>What the plugin says it does with the computer (shown before installing).</summary>
    public List<PluginPermission> Permissions { get; set; } = [];

    /// <summary>Where updates come from, if anywhere.</summary>
    public PluginUpdateSource? Update { get; set; }

    /// <summary>Its triggers, conditions and actions.</summary>
    public List<ManifestComponent> Components { get; set; } = [];
}

/// <summary>Where a plugin's updates are published.</summary>
/// <param name="GitHub">The GitHub repository, <c>owner/name</c>.</param>
/// <param name="Asset">The release file to download, as a wildcard pattern (default <c>*.aspkg</c>).</param>
public sealed record PluginUpdateSource(string GitHub, string Asset = "*.aspkg");

/// <summary>One trigger, condition or action in a manifest.</summary>
public sealed class ManifestComponent
{
    /// <summary>Type used in YAML, for example <c>acme.usb.eject</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Trigger, condition or action.</summary>
    public ComponentKind Kind { get; set; }

    /// <summary>Short title.</summary>
    public LocalizedString Title { get; set; } = LocalizedString.Of("");

    /// <summary>What it does.</summary>
    public LocalizedString Description { get; set; } = LocalizedString.Of("");

    /// <summary>Group in the editor; defaults to the plugin name.</summary>
    public string? Category { get; set; }

    /// <summary>For actions: where it runs.</summary>
    public ExecutionScope RunsAs { get; set; } = ExecutionScope.User;

    /// <summary>For actions: whether profiles can undo it (capture and restore).</summary>
    public bool Revertible { get; set; }

    /// <summary>For triggers and conditions: which files may use it.</summary>
    public ScopeSupport AvailableIn { get; set; } = ScopeSupport.Both;

    /// <summary>For triggers: the plugin event name (defaults to the type).</summary>
    public string? Event { get; set; }

    /// <summary>For triggers: the event that undoes this one.</summary>
    public string? Opposite { get; set; }

    /// <summary>YAML example.</summary>
    public string? Example { get; set; }

    /// <summary>Notes for the docs.</summary>
    public string? Notes { get; set; }

    /// <summary>Parameters.</summary>
    public List<ManifestField> Fields { get; set; } = [];

    /// <summary>For script plugins: the scripts, relative to the plugin folder.</summary>
    public ManifestScripts Scripts { get; set; } = new();

    /// <summary>For script triggers: how often <see cref="ManifestScripts.Poll"/> runs (default 30 seconds).</summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>How long a call may take before it is stopped (default 60 seconds).</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Where the component starts in <c>plugin.yaml</c>.</summary>
    public SourceLocation? Location { get; set; }
}

/// <summary>A field (parameter) of a manifest component.</summary>
public sealed class ManifestField
{
    /// <summary>Name in YAML.</summary>
    public string Name { get; set; } = "";

    /// <summary>Value type.</summary>
    public FieldType Type { get; set; } = FieldType.String;

    /// <summary>What it means.</summary>
    public LocalizedString Description { get; set; } = LocalizedString.Of("");

    /// <summary>Whether it must be set.</summary>
    public bool Required { get; set; }

    /// <summary>Default value as written in YAML.</summary>
    public object? Default { get; set; }

    /// <summary>Allowed values (enums and lists).</summary>
    public List<string>? Values { get; set; }

    /// <summary>Smallest allowed number.</summary>
    public double? Minimum { get; set; }

    /// <summary>Largest allowed number.</summary>
    public double? Maximum { get; set; }

    /// <summary>Example value.</summary>
    public string? Example { get; set; }

    /// <summary>Whether it tells settings apart (revertible actions).</summary>
    public bool Key { get; set; }

    /// <summary>Whether placeholders are replaced in it (text fields).</summary>
    public bool Placeholders { get; set; } = true;
}

/// <summary>The scripts of a script plugin component.</summary>
public sealed class ManifestScripts
{
    /// <summary>Actions: runs the action.</summary>
    public string? Apply { get; set; }

    /// <summary>Revertible actions: prints the current value.</summary>
    public string? Capture { get; set; }

    /// <summary>Revertible actions: restores a value printed by <see cref="Capture"/>.</summary>
    public string? Restore { get; set; }

    /// <summary>Conditions: prints <c>true</c> or <c>false</c>.</summary>
    public string? Evaluate { get; set; }

    /// <summary>Triggers: runs every interval and prints events.</summary>
    public string? Poll { get; set; }

    /// <summary>All scripts that are set.</summary>
    public IEnumerable<string> All => new[] { Apply, Capture, Restore, Evaluate, Poll }.OfType<string>();
}
