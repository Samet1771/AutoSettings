namespace AutoSettings.Core.Plugins;

/// <summary>
/// What a plugin says it does with the computer. AutoSettings shows these before installing a plugin. They are a
/// promise by the plugin's author, not a sandbox: a plugin runs with the full rights of its user (or of SYSTEM for
/// machine components), so only install plugins you trust.
/// </summary>
public enum PluginPermission
{
    /// <summary>Has components that run as SYSTEM (machine plugins only).</summary>
    RunAsSystem,
    /// <summary>Uses the network or the internet.</summary>
    Network,
    /// <summary>Changes machine-wide registry settings (HKEY_LOCAL_MACHINE).</summary>
    RegistryMachine,
    /// <summary>Changes files outside the user's own folders.</summary>
    FileSystemMachine,
    /// <summary>Starts other programs.</summary>
    ProcessLaunch,
    /// <summary>Runs PowerShell scripts (every script plugin does).</summary>
    PowerShell,
}

/// <summary>Names and descriptions of <see cref="PluginPermission"/> values.</summary>
public static class PluginPermissions
{
    private static readonly (PluginPermission Permission, string Name, string Description)[] Table =
    [
        (PluginPermission.RunAsSystem, "run_as_system", "Runs parts of itself as SYSTEM, with full control of this computer."),
        (PluginPermission.Network, "network", "Uses the network or the internet."),
        (PluginPermission.RegistryMachine, "registry_machine", "Changes machine-wide registry settings."),
        (PluginPermission.FileSystemMachine, "filesystem_machine", "Changes files outside your own folders."),
        (PluginPermission.ProcessLaunch, "process_launch", "Starts other programs."),
        (PluginPermission.PowerShell, "powershell", "Runs PowerShell scripts."),
    ];

    /// <summary>All names as written in <c>plugin.yaml</c>.</summary>
    public static IEnumerable<string> Names => Table.Select(t => t.Name);

    /// <summary>The name used in <c>plugin.yaml</c>, for example <c>run_as_system</c>.</summary>
    public static string Name(PluginPermission permission) => Table.First(t => t.Permission == permission).Name;

    /// <summary>A plain-language description for the install dialog.</summary>
    public static string Describe(PluginPermission permission) => Table.First(t => t.Permission == permission).Description;

    /// <summary>Parses a name from <c>plugin.yaml</c>.</summary>
    public static PluginPermission? Parse(string? name) =>
        Table.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) is { Name: not null } hit
            ? hit.Permission
            : null;
}
