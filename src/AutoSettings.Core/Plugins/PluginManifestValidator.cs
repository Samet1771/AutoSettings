using System.Text.RegularExpressions;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Model;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Plugins;

/// <summary>Checks that a <see cref="PluginManifest"/> makes sense and is safe to load.</summary>
public static partial class PluginManifestValidator
{
    /// <summary>SDK major versions this version of AutoSettings can load.</summary>
    public static readonly IReadOnlyList<int> SupportedSdkMajorVersions = [1];

    /// <summary>Field names plugins may not use: the engine gives them a meaning of its own.</summary>
    public static readonly IReadOnlyList<string> ReservedFieldNames = ["type", "user", ComponentCatalog.ContinueOnError.Name];

    /// <summary>
    /// Validates <paramref name="manifest"/>. <paramref name="appVersion"/> is the running AutoSettings version,
    /// used to check <c>min_app_version</c> (skipped when null).
    /// </summary>
    public static List<ConfigIssue> Validate(PluginManifest manifest, SemVersion? appVersion = null)
    {
        var issues = new List<ConfigIssue>();
        void Error(string message, SourceLocation? location = null) => issues.Add(new ConfigIssue(IssueSeverity.Error, message, location));
        void Warning(string message, SourceLocation? location = null) => issues.Add(new ConfigIssue(IssueSeverity.Warning, message, location));

        if (!PluginIdPattern().IsMatch(manifest.Id))
            Error($"id: '{manifest.Id}' is not a valid plugin id. Use publisher.name with lower-case letters, digits and dashes, for example 'acme.usb-tools'.");
        if (string.IsNullOrWhiteSpace(manifest.Name.English))
            Error("name: required.");
        if (string.IsNullOrWhiteSpace(manifest.Publisher))
            Error("publisher: required.");
        if (SemVersion.TryParse(manifest.Version) is null)
            Error($"version: '{manifest.Version}' is not a version such as 1.0.0.");

        if (!SdkPattern().IsMatch(manifest.Sdk))
            Error($"sdk: '{manifest.Sdk}' is not an SDK version such as \"1.0\".");
        else if (!SupportedSdkMajorVersions.Contains(int.Parse(manifest.Sdk.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture)))
            Error($"sdk: this version of {Product.Name} supports SDK {string.Join(", ", SupportedSdkMajorVersions.Select(v => v + ".x"))}, not {manifest.Sdk}. Update {Product.Name} or the plugin.");

        if (manifest.MinAppVersion is { } min)
        {
            if (SemVersion.TryParse(min) is not { } required)
                Error($"min_app_version: '{min}' is not a version such as 0.3.0.");
            // A beta of the required version counts as that version, so plugins can be tried on betas.
            else if (appVersion is not null && SemVersion.Parse($"{appVersion.Major}.{appVersion.Minor}.{appVersion.Patch}") < required)
                Error($"The plugin needs {Product.Name} {required} or newer; this is {appVersion}.");
        }

        switch (manifest.Kind)
        {
            case PluginKind.Dotnet:
                if (string.IsNullOrWhiteSpace(manifest.Entry))
                    Error("entry: required for .NET plugins (the plugin's .dll, for example Acme.Usb.dll).");
                else if (!IsSafeRelativePath(manifest.Entry) || !manifest.Entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    Error($"entry: '{manifest.Entry}' must be a .dll inside the plugin folder.");
                break;
            case PluginKind.Script:
                if (manifest.Entry is not null)
                    Warning("entry: ignored for script plugins.");
                if (!manifest.Permissions.Contains(PluginPermission.PowerShell))
                    manifest.Permissions.Add(PluginPermission.PowerShell);
                break;
        }

        var hasMachineComponents = manifest.Components.Any(c => c.Kind == ComponentKind.Action && c.RunsAs == ExecutionScope.Machine);
        if (hasMachineComponents && manifest.Scope != ExecutionScope.Machine)
            Error("Components with 'runs_as: machine' need 'scope: machine' (a plugin an administrator installs for the whole computer).");
        if (hasMachineComponents && !manifest.Permissions.Contains(PluginPermission.RunAsSystem))
            Error("Components with 'runs_as: machine' run as SYSTEM: add 'run_as_system' to 'permissions' so users are told before installing.");

        if (manifest.Update is { } update && !GitHubRepositoryPattern().IsMatch(update.GitHub))
            Error($"update.github: '{update.GitHub}' is not a GitHub repository such as owner/name.");

        if (manifest.Components.Count == 0)
            Error("components: the plugin has no triggers, conditions or actions.");

        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in manifest.Components)
            ValidateComponent(manifest, component, types, Error, Warning);

        var triggerEvents = manifest.Components.Where(c => c.Kind == ComponentKind.Trigger).Select(c => c.Event ?? c.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var component in manifest.Components.Where(c => c.Opposite is not null))
        {
            if (!triggerEvents.Contains(component.Opposite!))
                Warning($"{component.Type}: 'opposite' names '{component.Opposite}', which is not an event of this plugin.", component.Location);
        }
        return issues;
    }

    private static void ValidateComponent(
        PluginManifest manifest,
        ManifestComponent component,
        HashSet<string> types,
        Action<string, SourceLocation?> error,
        Action<string, SourceLocation?> warning)
    {
        var label = string.IsNullOrEmpty(component.Type) ? "component" : component.Type;
        var at = component.Location;

        if (!ComponentTypePattern().IsMatch(component.Type) || !component.Type.StartsWith(manifest.Id + ".", StringComparison.Ordinal)
            || component.Type.Count(c => c == '.') != manifest.Id.Count(c => c == '.') + 1)
        {
            error($"{label}: the type must be the plugin id, a dot and a name of lower-case letters, digits and underscores, for example '{manifest.Id}.do_something'.", at);
        }
        if (!types.Add(component.Type))
            error($"{label}: another component already uses this type.", at);
        if (string.IsNullOrWhiteSpace(component.Title.English))
            error($"{label}: 'title' is required.", at);
        if (string.IsNullOrWhiteSpace(component.Description.English))
            error($"{label}: 'description' is required.", at);

        if (component.Kind != ComponentKind.Action && component.RunsAs == ExecutionScope.Machine)
            error($"{label}: only actions have 'runs_as'.", at);
        if (component.Kind != ComponentKind.Action && component.Revertible)
            error($"{label}: only actions can be revertible.", at);
        if (component.Kind != ComponentKind.Trigger && (component.Event is not null || component.Opposite is not null || component.Interval is not null))
            error($"{label}: 'event', 'opposite' and 'interval' are only for triggers.", at);
        if (component.Event is { } name && (!ComponentTypePattern().IsMatch(name) || !name.StartsWith(manifest.Id + ".", StringComparison.Ordinal)))
            error($"{label}: the event name must start with the plugin id, for example '{manifest.Id}.changed'.", at);
        if (component.Interval is { } interval && interval < TimeSpan.FromSeconds(5))
            error($"{label}: 'interval' must be at least 5s.", at);
        if (component.Timeout is { } timeout && (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(30)))
            error($"{label}: 'timeout' must be between 1s and 30m.", at);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in component.Fields)
        {
            var fieldLabel = $"{label}, field '{field.Name}'";
            if (!FieldNamePattern().IsMatch(field.Name))
                error($"{fieldLabel}: names use lower-case letters, digits and underscores and start with a letter.", at);
            else if (ReservedFieldNames.Contains(field.Name))
                error($"{fieldLabel}: '{field.Name}' is reserved.", at);
            if (!names.Add(field.Name))
                error($"{fieldLabel}: declared twice.", at);
            if (string.IsNullOrWhiteSpace(field.Description.English))
                error($"{fieldLabel}: 'description' is required.", at);
            if (field.Type == FieldType.Enum && (field.Values is null || field.Values.Count == 0))
                error($"{fieldLabel}: enum fields need 'values'.", at);
            if (field.Values is not null && field.Type is not (FieldType.Enum or FieldType.StringList))
                error($"{fieldLabel}: 'values' is only for enum and string_list fields.", at);
            if ((field.Minimum is not null || field.Maximum is not null) && field.Type is not (FieldType.Integer or FieldType.Number))
                error($"{fieldLabel}: 'min' and 'max' are only for integer and number fields.", at);
            if (field.Key && component.Kind != ComponentKind.Action)
                error($"{fieldLabel}: 'key' is only for actions.", at);
            if (field.Required && field.Default is not null)
                warning($"{fieldLabel}: a required field does not need a default.", at);
            if (field.Default is not null && !ValueConverter.TryConvert(field.Default, ManifestMapping.ToFieldDescriptor(field), out _, out var problem))
                error($"{fieldLabel}: the default is not valid: {problem}.", at);
            if (component.Kind == ComponentKind.Trigger && field.Default is not null)
                warning($"{fieldLabel}: a trigger field with a default always filters events.", at);
        }

        if (manifest.Kind == PluginKind.Script)
        {
            var scripts = component.Scripts;
            switch (component.Kind)
            {
                case ComponentKind.Action:
                    if (scripts.Apply is null)
                        error($"{label}: script actions need 'scripts.apply'.", at);
                    if (component.Revertible && (scripts.Capture is null || scripts.Restore is null))
                        error($"{label}: revertible script actions need 'scripts.capture' and 'scripts.restore'.", at);
                    if (scripts.Evaluate is not null || scripts.Poll is not null)
                        error($"{label}: actions use 'apply', 'capture' and 'restore' only.", at);
                    break;
                case ComponentKind.Condition:
                    if (scripts.Evaluate is null)
                        error($"{label}: script conditions need 'scripts.evaluate'.", at);
                    if (scripts.All.Count() != 1)
                        error($"{label}: conditions use 'evaluate' only.", at);
                    break;
                case ComponentKind.Trigger:
                    // A trigger can be raised by the poll script of another trigger of the plugin (for example
                    // "disconnected" by the script that also raises "connected"), so a poll script is optional.
                    if (scripts.Apply is not null || scripts.Capture is not null || scripts.Restore is not null || scripts.Evaluate is not null)
                        error($"{label}: triggers use 'poll' only.", at);
                    break;
            }
            foreach (var script in scripts.All)
            {
                if (!IsSafeRelativePath(script) || !script.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                    error($"{label}: '{script}' must be a .ps1 file inside the plugin folder.", at);
            }
        }
        else if (component.Scripts.All.Any() || component.Interval is not null)
        {
            error($"{label}: 'scripts' and 'interval' are only for script plugins.", at);
        }
    }

    /// <summary>Whether <paramref name="path"/> stays inside the plugin folder (relative, no <c>..</c>, no drive or root).</summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.StartsWith('\\') || path.StartsWith('/'))
            return false;
        return path.Split('/', '\\').All(part => part.Length > 0 && part != ".." && part != ".");
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\.[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex PluginIdPattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)*\.[a-z][a-z0-9_]*$")]
    private static partial Regex ComponentTypePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex FieldNamePattern();

    [GeneratedRegex(@"^\d+(\.\d+){0,2}$")]
    private static partial Regex SdkPattern();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex GitHubRepositoryPattern();
}
