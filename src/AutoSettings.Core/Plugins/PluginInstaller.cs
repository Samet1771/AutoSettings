using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Updates;

namespace AutoSettings.Core.Plugins;

/// <summary>A plugin could not be installed, removed or changed. The message says why, in plain language.</summary>
public sealed class PluginInstallException(string message) : Exception(message);

/// <summary>
/// Installs, removes, turns on and off and rolls back plugins in a plugin folder. Every change is made so that a
/// running AutoSettings never sees a half-copied plugin: packages are unpacked into a staging folder next to the
/// plugins and moved into place in one step; old versions are moved aside before they are deleted.
/// </summary>
/// <remarks>The caller must have write access to the folder (administrators for machine plugins).</remarks>
public static class PluginInstaller
{
    private const string StagingFolder = ".staging";
    private const string TrashFolder = "_trash";

    /// <summary>Installs (or updates) the plugin in <paramref name="packagePath"/> into <paramref name="root"/>.</summary>
    /// <param name="root">The plugin folder (machine or user).</param>
    /// <param name="packagePath">The <c>.aspkg</c> file.</param>
    /// <param name="scope">Whether <paramref name="root"/> holds machine or user plugins.</param>
    /// <param name="appVersion">The running app version, for <c>min_app_version</c>.</param>
    /// <param name="source">Where the package came from (shown in the app).</param>
    /// <exception cref="PluginInstallException">The package is not valid or cannot be installed here.</exception>
    public static (PluginManifest Manifest, InstalledPluginEntry Entry) Install(
        string root, string packagePath, ExecutionScope scope, SemVersion? appVersion, string? source = null)
    {
        var (manifest, issues) = PluginPackage.Inspect(packagePath, appVersion);
        var errors = issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message).ToList();
        if (manifest is null || errors.Count > 0)
            throw new PluginInstallException(string.Join(" ", errors.Take(3)) + (errors.Count > 3 ? $" (and {errors.Count - 3} more)" : ""));
        if (scope == ExecutionScope.User && manifest.Scope == ExecutionScope.Machine)
            throw new PluginInstallException($"{manifest.Name} is a machine plugin: install it for all users (as an administrator).");

        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, StagingFolder, Guid.NewGuid().ToString("N"));
        try
        {
            PluginPackage.Extract(packagePath, staging);
        }
        catch (InvalidDataException ex)
        {
            TryDelete(staging);
            throw new PluginInstallException(ex.Message);
        }

        var pluginFolder = Path.Combine(root, manifest.Id);
        var target = Path.Combine(pluginFolder, manifest.Version);
        Directory.CreateDirectory(pluginFolder);
        try
        {
            if (Directory.Exists(target))
                MoveToTrash(root, target);
            Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(staging);
            throw new PluginInstallException($"Version {manifest.Version} of {manifest.Name} is in use. Turn the plugin off, wait a few seconds and try again.");
        }

        var state = InstalledPluginsFile.Read(root);
        var entry = state.Find(manifest.Id);
        string? previous = null;
        if (entry is null)
        {
            entry = new InstalledPluginEntry { Id = manifest.Id };
            state.Plugins.Add(entry);
        }
        else if (!string.Equals(entry.Version, manifest.Version, StringComparison.OrdinalIgnoreCase)
                 && Directory.Exists(Path.Combine(pluginFolder, entry.Version)))
        {
            previous = entry.Version;
        }
        entry.Version = manifest.Version;
        entry.Remove = false;
        entry.Sha256 = PluginPackage.Sha256(packagePath);
        entry.Installed = DateTimeOffset.Now;
        entry.Previous = previous ?? (entry.Previous is { } kept && Directory.Exists(Path.Combine(pluginFolder, kept)) && kept != manifest.Version ? kept : null);
        entry.Source = source ?? Path.GetFileName(packagePath);

        // Keep only the new version and the one before it.
        foreach (var folder in Directory.GetDirectories(pluginFolder))
        {
            var name = Path.GetFileName(folder);
            if (string.Equals(name, entry.Version, StringComparison.OrdinalIgnoreCase) || string.Equals(name, entry.Previous, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                MoveToTrash(root, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An old version still in use: it is not loaded any more, and goes with a later update.
            }
        }
        state.Write(root);
        CleanUp(root);
        return (manifest, entry);
    }

    /// <summary>
    /// Removes a plugin and all its versions. When its files are in use (a .NET plugin that is still running), it is
    /// marked for removal instead: it stops loading at once and its files go the next time plugins are loaded.
    /// Returns false when it was not installed.
    /// </summary>
    public static bool Uninstall(string root, string id)
    {
        if (!PluginManifestValidator.IsSafeRelativePath(id))
            return false;
        var state = InstalledPluginsFile.Read(root);
        var entry = state.Find(id);
        var folder = Path.Combine(root, id);
        if (entry is null && !Directory.Exists(folder))
            return false;
        try
        {
            if (Directory.Exists(folder))
                MoveToTrash(root, folder);
            state.Plugins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entry ??= AddEntry(state, id, root);
            entry.Remove = true;
        }
        state.Write(root);
        CleanUp(root);
        return true;
    }

    /// <summary>Deletes plugins that were uninstalled while in use. Call it after their hosts were stopped.</summary>
    public static void FinishRemovals(string root)
    {
        var state = InstalledPluginsFile.Read(root);
        var pending = state.Plugins.Where(p => p.Remove).ToList();
        if (pending.Count == 0)
            return;
        foreach (var entry in pending)
        {
            try
            {
                var folder = Path.Combine(root, entry.Id);
                if (Directory.Exists(folder))
                    MoveToTrash(root, folder);
                state.Plugins.Remove(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use; try again next time.
            }
        }
        state.Write(root);
        CleanUp(root);
    }

    private static InstalledPluginEntry AddEntry(InstalledPluginsFile state, string id, string root)
    {
        var version = Directory.Exists(Path.Combine(root, id))
            ? Directory.GetDirectories(Path.Combine(root, id)).Select(d => Path.GetFileName(d)).FirstOrDefault() ?? ""
            : "";
        var entry = new InstalledPluginEntry { Id = id, Version = version };
        state.Plugins.Add(entry);
        return entry;
    }

    /// <summary>Turns a plugin on or off without removing it.</summary>
    /// <exception cref="PluginInstallException">The plugin is not installed.</exception>
    public static void SetEnabled(string root, string id, bool enabled)
    {
        var state = InstalledPluginsFile.Read(root);
        var entry = state.Find(id) ?? AdoptManualInstall(root, id, state);
        entry.Enabled = enabled;
        state.Write(root);
    }

    /// <summary>Goes back to the version used before the last update. Returns that version.</summary>
    /// <exception cref="PluginInstallException">There is no earlier version to go back to.</exception>
    public static string Rollback(string root, string id)
    {
        var state = InstalledPluginsFile.Read(root);
        var entry = state.Find(id) ?? throw new PluginInstallException($"{id} is not installed.");
        if (entry.Previous is not { } previous || !Directory.Exists(Path.Combine(root, id, previous)))
            throw new PluginInstallException($"There is no earlier version of {id} to go back to.");
        (entry.Version, entry.Previous) = (previous, entry.Version);
        state.Write(root);
        return previous;
    }

    /// <summary>Deletes what earlier changes moved aside (files a running plugin still used may stay until next time).</summary>
    public static void CleanUp(string root)
    {
        foreach (var folder in new[] { Path.Combine(root, TrashFolder), Path.Combine(root, StagingFolder) })
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var child in Directory.GetDirectories(folder))
                TryDelete(child);
        }
    }

    /// <summary>A plugin that was copied in by hand gets an entry, so it can be turned off.</summary>
    private static InstalledPluginEntry AdoptManualInstall(string root, string id, InstalledPluginsFile state)
    {
        var plugin = PluginStore.Discover(root, ExecutionScope.User).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new PluginInstallException($"{id} is not installed.");
        var entry = new InstalledPluginEntry { Id = plugin.Id, Version = Path.GetFileName(plugin.Directory), Source = "copied by hand" };
        state.Plugins.Add(entry);
        return entry;
    }

    private static void MoveToTrash(string root, string folder)
    {
        var trash = Path.Combine(root, TrashFolder);
        Directory.CreateDirectory(trash);
        Directory.Move(folder, Path.Combine(trash, $"{Path.GetFileName(Path.GetDirectoryName(folder))}-{Path.GetFileName(folder)}-{Guid.NewGuid():N}"));
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use by a plugin that is still running; the next clean-up removes it.
        }
    }
}
