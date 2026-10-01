using System.Text.Json;
using System.Text.Json.Serialization;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Plugins;

/// <summary>A plugin found on disk.</summary>
public sealed class InstalledPlugin
{
    /// <summary>The plugin id (the folder name).</summary>
    public required string Id { get; init; }

    /// <summary>The folder of the version in use, which contains <c>plugin.yaml</c>.</summary>
    public required string Directory { get; init; }

    /// <summary>Installed for the machine or for one user.</summary>
    public required ExecutionScope Scope { get; init; }

    /// <summary>The manifest, or <c>null</c> when it could not be read.</summary>
    public PluginManifest? Manifest { get; init; }

    /// <summary>Problems with the manifest or the folder. A plugin with errors is not loaded.</summary>
    public List<ConfigIssue> Issues { get; init; } = [];

    /// <summary>Whether the user turned it on (plugins are on unless turned off).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The SHA-256 of the package it was installed from, when installed from a package.</summary>
    public string? PackageSha256 { get; init; }

    /// <summary>Whether it has no errors and can be loaded.</summary>
    public bool IsValid => Manifest is not null && !Issues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>Whether it is valid and turned on.</summary>
    public bool IsActive => IsValid && Enabled;

    /// <summary>A display name: the manifest name, or the id.</summary>
    public string DisplayName => Manifest?.Name.English is { Length: > 0 } name ? name : Id;
}

/// <summary>The state kept in <c>installed.json</c> in each plugin folder: which version is used and whether it is on.</summary>
public sealed class InstalledPluginsFile
{
    /// <summary>File name inside the plugin folder.</summary>
    public const string FileName = "installed.json";

    /// <summary>One entry per plugin id.</summary>
    [JsonPropertyName("plugins")]
    public List<InstalledPluginEntry> Plugins { get; set; } = [];

    /// <summary>The entry for <paramref name="id"/>, or <c>null</c>.</summary>
    public InstalledPluginEntry? Find(string id) =>
        Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the file in <paramref name="root"/>; a missing or damaged file gives an empty list.</summary>
    public static InstalledPluginsFile Read(string root)
    {
        var path = Path.Combine(root, FileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<InstalledPluginsFile>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    /// <summary>Writes the file to <paramref name="root"/> (through a temporary file, so it is never half written).</summary>
    public void Write(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>One plugin in <c>installed.json</c>.</summary>
public sealed class InstalledPluginEntry
{
    /// <summary>Plugin id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The version in use (a subfolder of the plugin's folder).</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>Whether it is turned on.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>SHA-256 of the package it came from.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>When it was installed.</summary>
    [JsonPropertyName("installed")]
    public DateTimeOffset? Installed { get; set; }
}

/// <summary>
/// Finds installed plugins. A plugin folder looks like <c>&lt;root&gt;\&lt;id&gt;\&lt;version&gt;\plugin.yaml</c>; the version in
/// use is the one named in <c>installed.json</c>, or else the highest version found (so a folder copied in by hand works).
/// </summary>
public static class PluginStore
{
    /// <summary>The manifest file name.</summary>
    public const string ManifestFileName = "plugin.yaml";

    /// <summary>Finds the plugins in <paramref name="root"/>.</summary>
    /// <param name="root">The plugin folder.</param>
    /// <param name="scope">Whether it holds machine or user plugins.</param>
    /// <param name="appVersion">The running app version, to check <c>min_app_version</c>.</param>
    public static List<InstalledPlugin> Discover(string root, ExecutionScope scope, SemVersion? appVersion = null)
    {
        var result = new List<InstalledPlugin>();
        if (!Directory.Exists(root))
            return result;
        var state = InstalledPluginsFile.Read(root);

        foreach (var pluginDirectory in SafeDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(pluginDirectory);
            if (id.StartsWith('.') || id.StartsWith('_'))
                continue; // staging and backup folders of the installer
            var entry = state.Find(id);
            var versions = SafeDirectories(pluginDirectory)
                .Where(d => File.Exists(Path.Combine(d, ManifestFileName)))
                .Select(d => (Directory: d, Version: SemVersion.TryParse(Path.GetFileName(d))))
                .ToList();

            var chosen = entry is not null
                ? versions.FirstOrDefault(v => string.Equals(Path.GetFileName(v.Directory), entry.Version, StringComparison.OrdinalIgnoreCase))
                : versions.Where(v => v.Version is not null).OrderByDescending(v => v.Version).FirstOrDefault();
            if (chosen.Directory is null)
            {
                if (versions.Count > 0 || entry is not null)
                {
                    result.Add(new InstalledPlugin
                    {
                        Id = id,
                        Directory = pluginDirectory,
                        Scope = scope,
                        Enabled = entry?.Enabled ?? true,
                        Issues = [new ConfigIssue(IssueSeverity.Error, entry is not null
                            ? $"The installed version {entry.Version} is missing from {pluginDirectory}."
                            : $"No version folder (such as 1.0.0) with a {ManifestFileName} was found in {pluginDirectory}.")],
                    });
                }
                continue;
            }
            result.Add(Load(chosen.Directory, id, scope, entry, appVersion));
        }
        return result;
    }

    /// <summary>Reads and checks one plugin version folder.</summary>
    public static InstalledPlugin Load(string directory, string id, ExecutionScope scope, InstalledPluginEntry? entry = null, SemVersion? appVersion = null)
    {
        PluginManifest? manifest = null;
        var issues = new List<ConfigIssue>();
        try
        {
            (manifest, issues) = PluginManifestReader.Load(File.ReadAllText(Path.Combine(directory, ManifestFileName)), appVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new ConfigIssue(IssueSeverity.Error, $"Cannot read {ManifestFileName}: {ex.Message}"));
        }

        if (manifest is not null)
        {
            if (!string.Equals(manifest.Id, id, StringComparison.Ordinal))
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"The folder is named '{id}' but {ManifestFileName} says the id is '{manifest.Id}'."));
            if (!string.Equals(manifest.Version, Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase))
                issues.Add(new ConfigIssue(IssueSeverity.Error, $"The folder is named '{Path.GetFileName(directory)}' but {ManifestFileName} says the version is '{manifest.Version}'."));
            if (scope == ExecutionScope.User && manifest.Scope == ExecutionScope.Machine)
                issues.Add(new ConfigIssue(IssueSeverity.Error, "This is a machine plugin; an administrator must install it for the whole computer."));
            foreach (var file in manifest.Components.SelectMany(c => c.Scripts.All).Append(manifest.Entry).OfType<string>().Distinct())
            {
                if (PluginManifestValidator.IsSafeRelativePath(file) && !File.Exists(Path.Combine(directory, file)))
                    issues.Add(new ConfigIssue(IssueSeverity.Error, $"{file} is missing from the plugin folder."));
            }
        }

        return new InstalledPlugin
        {
            Id = id,
            Directory = directory,
            Scope = scope,
            Manifest = manifest,
            Issues = issues,
            Enabled = entry?.Enabled ?? true,
            PackageSha256 = entry?.Sha256,
        };
    }

    /// <summary>
    /// Builds the catalog from the built-in components and the active plugins. Machine plugins come first, so a user
    /// plugin cannot replace one. Plugins that clash are left out and reported in <see cref="CatalogComposition.Rejected"/>.
    /// </summary>
    public static CatalogComposition Compose(IEnumerable<InstalledPlugin> plugins) =>
        ComponentCatalog.Compose(
            BuiltInComponents.All,
            plugins.Where(p => p.IsActive)
                .OrderBy(p => p.Scope == ExecutionScope.Machine ? 0 : 1)
                .Select(p => ManifestMapping.ToContribution(p.Manifest!, p.Scope)));

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
