using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AutoSettings.Agent.Dialogs;
using AutoSettings.Agent.Localization;
using AutoSettings.Agent.Plugins;
using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Plugins;
using AutoSettings.Core.Updates;
using AutoSettings.Platform.Plugins;
using AutoSettings.Platform.Updates;
using Microsoft.Win32;

namespace AutoSettings.Agent;

/// <summary>One row in the plugin list.</summary>
public sealed record PluginRow(string Key, string Id, ExecutionScope Scope, bool Enabled, string Name, string Version, string Publisher, string For, string Kind, string Status);

/// <summary>The Plugins page.</summary>
public partial class MainWindow
{
    private List<PluginUpdate> _pluginUpdates = [];
    private bool _pluginsBusy;
    private bool _checkedPluginUpdates;

    private PluginRow? SelectedPlugin => PluginList.SelectedItem as PluginRow;

    private void OnPluginsPageShown()
    {
        RefreshPlugins();
        if (!_checkedPluginUpdates)
        {
            _checkedPluginUpdates = true;
            _ = CheckPluginUpdatesAsync(quiet: true);
        }
    }

    private void RefreshPlugins()
    {
        var plugins = _host.Plugins.Plugins;
        var loaded = _host.Plugins.Catalog.All.Select(d => d.Source.PluginId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = SelectedPlugin?.Key;
        var rows = plugins.Select(p => ToRow(p, loaded)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        PluginList.ItemsSource = rows;
        PluginList.SelectedItem = rows.FirstOrDefault(r => r.Key == selected);
        if (!_pluginsBusy && string.IsNullOrEmpty(PluginStatus.Text) && rows.Count == 0)
            PluginStatus.Text = Strings.Get("NoPlugins");
    }

    private PluginRow ToRow(InstalledPlugin plugin, HashSet<string> loaded)
    {
        var manifest = plugin.Manifest;
        string status;
        if (!plugin.IsValid)
            status = Strings.Format("PluginError", plugin.Issues.FirstOrDefault(i => i.Severity == IssueSeverity.Error)?.Message ?? "?");
        else if (!plugin.Enabled)
            status = Strings.Get("Off");
        else if (!loaded.Contains(plugin.Id))
            status = Strings.Get("PluginNotLoaded");
        else
            status = Strings.Get("Active");
        if (_pluginUpdates.FirstOrDefault(u => u.Plugin.Id == plugin.Id && u.Plugin.Scope == plugin.Scope) is { } update)
            status += " · " + Strings.Format("PluginUpdateAvailable", update.Release.Version);

        var kind = manifest?.Kind switch
        {
            PluginKind.Script => "PowerShell",
            PluginKind.Dotnet => ".NET, " + Signature(plugin),
            _ => "",
        };
        var name = manifest is null ? plugin.Id : Localized(manifest.Name);
        return new PluginRow(
            $"{plugin.Scope}:{plugin.Id}",
            plugin.Id,
            plugin.Scope,
            plugin.Enabled,
            name,
            manifest?.Version ?? "?",
            manifest?.Publisher ?? "",
            Strings.Get(plugin.Scope == ExecutionScope.Machine ? "ForEveryone" : "ForMe"),
            kind,
            status);
    }

    private static string Localized(LocalizedString text) =>
        text.Translations.GetValueOrDefault(Strings.Culture.TwoLetterISOLanguageName) ?? text.English;

    private static string Signature(InstalledPlugin plugin)
    {
        if (plugin.Manifest?.Entry is not { } entry)
            return Strings.Get("NotSigned");
        var signer = Authenticode.TrustedSigner(Path.Combine(plugin.Directory, entry));
        return signer is null ? Strings.Get("NotSigned") : Strings.Format("SignedBy", ShortName(signer));
    }

    /// <summary>"CN=Acme Ltd, O=Acme Ltd, C=TR" → "Acme Ltd".</summary>
    private static string ShortName(string subject)
    {
        var cn = subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
        return cn is null ? subject : cn[3..].Trim('"');
    }

    // ------------------------------------------------------------------ installing

    private async void OnInstallPluginFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Strings.Get("PluginPackageFilter"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            await InstallPackageAsync(dialog.FileName);
    }

    private async void OnInstallPluginGitHubClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PluginSourceWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Repository is not { } repository)
            return;
        var folder = Path.Combine(Path.GetTempPath(), "AutoSettings-plugins");
        string? file = null;
        await RunPluginTaskAsync(Strings.Format("Downloading", repository), async () =>
        {
            using var manager = new PluginManager();
            (file, _) = await manager.DownloadFromGitHubAsync(repository, folder, dialog.IncludeBetas);
        });
        if (file is null)
            return;
        try
        {
            await InstallPackageAsync(file);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Shows what the package is and what it may do, asks where to install it, and installs it.</summary>
    private async Task InstallPackageAsync(string path)
    {
        var (manifest, issues) = PluginPackage.Inspect(path, AppVersion.Current);
        var errors = issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message).ToList();
        if (manifest is null || errors.Count > 0)
        {
            ShowError(Strings.Format("PluginInvalid", string.Join(Environment.NewLine, errors.Take(5))));
            return;
        }

        var name = Localized(manifest.Name);
        ExecutionScope scope;
        if (manifest.Scope == ExecutionScope.Machine)
        {
            scope = ExecutionScope.Machine;
        }
        else
        {
            var where = MessageBox.Show(this, Strings.Format("InstallForEveryoneQuestion", name), Product.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (where == MessageBoxResult.Cancel)
                return;
            scope = where == MessageBoxResult.Yes ? ExecutionScope.Machine : ExecutionScope.User;
        }

        var text = new StringBuilder(Strings.Format("PluginInstallConfirm", name, manifest.Version, manifest.Publisher));
        if (manifest.Description is { } description)
            text.AppendLine().AppendLine().Append(Localized(description));
        text.AppendLine().AppendLine().Append(Strings.Get(scope == ExecutionScope.Machine ? "PluginForEveryoneNote" : "PluginForMeNote"));
        text.AppendLine().AppendLine();
        if (manifest.Permissions.Count == 0)
        {
            text.Append(Strings.Get("PluginNoPermissions"));
        }
        else
        {
            text.Append(Strings.Get("PluginPermissionsTitle"));
            foreach (var permission in manifest.Permissions)
                text.AppendLine().Append("•  ").Append(Strings.Get("Permission." + PluginPermissions.Name(permission)));
        }
        text.AppendLine().AppendLine().Append(Strings.Get("PluginTrust"));
        var risky = scope == ExecutionScope.Machine || manifest.Permissions.Count > 0;
        if (MessageBox.Show(this, text.ToString(), Product.Name, MessageBoxButton.OKCancel, risky ? MessageBoxImage.Warning : MessageBoxImage.Information) != MessageBoxResult.OK)
            return;

        await RunPluginCommandAsync(["install", path, "--scope", scope == ExecutionScope.Machine ? "machine" : "user"]);
    }

    // ------------------------------------------------------------------ managing

    private async void OnTogglePluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key } || _host.Plugins.Plugins.FirstOrDefault(p => $"{p.Scope}:{p.Id}" == key) is not { } plugin)
            return;
        await RunPluginCommandAsync([plugin.Enabled ? "disable" : "enable", plugin.Id, "--scope", ScopeName(plugin.Scope)]);
    }

    private async void OnUpdatePluginClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPlugin is not { } row)
            return;
        await RunPluginCommandAsync(["update", row.Id, "--scope", ScopeName(row.Scope)]);
        _pluginUpdates.RemoveAll(u => u.Plugin.Id == row.Id && u.Plugin.Scope == row.Scope);
    }

    private async void OnRollBackPluginClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPlugin is { } row)
            await RunPluginCommandAsync(["rollback", row.Id, "--scope", ScopeName(row.Scope)]);
    }

    private async void OnRemovePluginClick(object sender, RoutedEventArgs e)
    {
        if (SelectedPlugin is not { } row)
            return;
        if (MessageBox.Show(this, Strings.Format("PluginRemoveQuestion", row.Name), Product.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await RunPluginCommandAsync(["uninstall", row.Id, "--scope", ScopeName(row.Scope)]);
    }

    private async void OnCheckPluginUpdatesClick(object sender, RoutedEventArgs e) => await CheckPluginUpdatesAsync(quiet: false);

    private void OnOpenPluginFolderClick(object sender, RoutedEventArgs e) =>
        TrayIcon.OpenFolder(SelectedPlugin?.Scope == ExecutionScope.Machine ? Product.MachinePluginDirectory : Product.UserPluginDirectory);

    private async Task CheckPluginUpdatesAsync(bool quiet)
    {
        var plugins = _host.Plugins.Plugins.Where(p => p.IsValid).ToList();
        if (plugins.All(p => p.Manifest?.Update is null))
        {
            if (!quiet)
                PluginStatus.Text = Strings.Get("PluginsUpToDate");
            return;
        }
        await RunPluginTaskAsync(Strings.Get("UpdateStateChecking"), async () =>
        {
            using var manager = new PluginManager();
            _pluginUpdates = await manager.CheckForUpdatesAsync(plugins, includePrereleases: false, CancellationToken.None);
        });
        PluginStatus.Text = _pluginUpdates.Count == 0 ? Strings.Get("PluginsUpToDate") : Strings.Format("PluginUpdatesFound", _pluginUpdates.Count);
        RefreshPlugins();
    }

    private Task RunPluginCommandAsync(IReadOnlyList<string> args) => RunPluginTaskAsync(null, async () =>
    {
        var result = await PluginCommands.RunAsync(args);
        await Dispatcher.InvokeAsync(() => PluginStatus.Text = result.Message);
        if (!result.Succeeded && result.ExitCode != 3)
            await Dispatcher.InvokeAsync(() => ShowError(result.Message));
    });

    /// <summary>Runs a plugin operation with the toolbar disabled; errors are shown, never thrown.</summary>
    private async Task RunPluginTaskAsync(string? status, Func<Task> work)
    {
        if (_pluginsBusy)
            return;
        _pluginsBusy = true;
        PluginToolbar.IsEnabled = false;
        PluginList.IsEnabled = false;
        if (status is not null)
            PluginStatus.Text = status;
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex) when (ex is PluginInstallException or IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            PluginStatus.Text = ex.Message;
            ShowError(ex.Message);
        }
        finally
        {
            _pluginsBusy = false;
            PluginToolbar.IsEnabled = true;
            PluginList.IsEnabled = true;
        }
        // The plugin folders are watched; the list also refreshes when the plugins reload a moment later.
        RefreshPlugins();
    }

    private static string ScopeName(ExecutionScope scope) => scope == ExecutionScope.Machine ? "machine" : "user";
}
