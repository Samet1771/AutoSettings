using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoSettings.Agent.Dialogs;
using AutoSettings.Agent.Localization;
using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Editing;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Updates;
using Microsoft.Win32;

namespace AutoSettings.Agent;

/// <summary>One row in the automation list.</summary>
public sealed record AutomationRow(string Id, bool Enabled, string Name, string When, string If, string Then);

/// <summary>One row in the profile list.</summary>
public sealed record ProfileRow(string Id, string Status, string Name, int Priority, string Actions);

/// <summary>One row in the activity list.</summary>
public sealed record ActivityRow(string Icon, Brush Brush, string Time, string Message);

/// <summary>The main window: automations, profiles, templates, activity and settings.</summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly Brush InfoBrush = Brushes.SteelBlue;
    private static readonly Brush SuccessBrush = Brushes.SeaGreen;
    private static readonly Brush WarningBrush = Brushes.DarkOrange;
    private static readonly Brush ErrorBrush = Brushes.IndianRed;

    private readonly AgentHost _host;
    private readonly AgentSettings _settings;
    private readonly ObservableCollection<ActivityRow> _activity = [];
    private bool _loadingSettings;

    public MainWindow(AgentHost host, AgentSettings settings)
    {
        _host = host;
        _settings = settings;
        InitializeComponent();
        if (settings.Theme == "system")
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        ActivityList.ItemsSource = _activity;
        TemplateList.ItemsSource = Templates.All;
        LoadSettingsPage();

        host.Activity.EntryAdded += OnActivityAdded;
        host.StatusChanged += OnStatusChanged;
        host.Store.Loaded += OnConfigLoaded;
        host.UpdateStatusChanged += OnUpdateStatusChanged;
        Closed += (_, _) =>
        {
            host.Activity.EntryAdded -= OnActivityAdded;
            host.StatusChanged -= OnStatusChanged;
            host.Store.Loaded -= OnConfigLoaded;
            host.UpdateStatusChanged -= OnUpdateStatusChanged;
        };

        LoadPersonalActivity();
        Refresh();
        RefreshUpdates();
    }

    /// <summary>Opens the Settings page (for example from the update notification).</summary>
    public void ShowSettings() => ShowPage("settings");

    // ------------------------------------------------------------------ navigation and refresh

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutomationsPage is null || Navigation.SelectedItem is not ListBoxItem { Tag: string page })
            return;
        AutomationsPage.Visibility = page == "automations" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        TemplatesPage.Visibility = page == "templates" ? Visibility.Visible : Visibility.Collapsed;
        ActivityPage.Visibility = page == "activity" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "settings")
            StatusDetails.Text = BuildStatus();
    }

    private void ShowPage(string page)
    {
        foreach (var item in Navigation.Items.OfType<ListBoxItem>())
            item.IsSelected = (string)item.Tag == page;
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    private void OnConfigLoaded(object? sender, ConfigLoadResult result) => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        var engine = _host.Engine;
        var config = engine.Config;

        var selectedAutomations = AutomationList.SelectedItems.OfType<AutomationRow>().Select(r => r.Id).ToHashSet();
        var automationRows = config.Automations.Select(a => new AutomationRow(
            a.Id,
            a.Enabled,
            a.DisplayName + (engine.SuspendedAutomations.Contains(a.Id) ? " ⏸" : "")
                + (a.IsBlocked ? " ⚠ " + Strings.Format("NeedsPlugin", string.Join(", ", a.MissingPlugins)) : ""),
            ComponentSummary.DescribeAll(ComponentKind.Trigger, a.Triggers, Strings.Get("OrSeparator"), AgentCatalog.Current),
            a.Conditions.Count == 0 ? Strings.Get("Always") : ComponentSummary.DescribeAll(ComponentKind.Condition, a.Conditions, Strings.Get("AndSeparator"), AgentCatalog.Current),
            ComponentSummary.DescribeAll(ComponentKind.Action, a.Actions, " → ", AgentCatalog.Current))).ToList();
        AutomationList.ItemsSource = automationRows;
        foreach (var row in automationRows.Where(r => selectedAutomations.Contains(r.Id)))
            AutomationList.SelectedItems.Add(row);

        var active = engine.ActiveProfiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var selectedProfile = (ProfileList.SelectedItem as ProfileRow)?.Id;
        var profileRows = config.Profiles.Select(p => new ProfileRow(
            p.Id,
            active.TryGetValue(p.Id, out var info)
                ? "● " + (info.RevertsWhen is null ? Strings.Get("Active") : Strings.Format("ActiveReverts", info.RevertsWhen))
                : Strings.Get("Off"),
            p.DisplayName,
            p.Priority,
            ComponentSummary.DescribeAll(ComponentKind.Action, p.Actions, "; ", AgentCatalog.Current))).ToList();
        ProfileList.ItemsSource = profileRows;
        ProfileList.SelectedItem = profileRows.FirstOrDefault(r => r.Id == selectedProfile);

        var enabled = config.Automations.Count(a => a.Enabled);
        var state = engine.IsPaused
            ? engine.PausedUntil is { } until ? Strings.Format("PausedUntil", until.ToString("t", Strings.Culture)) : Strings.Get("Paused")
            : Strings.Format("AutomationsOn", enabled, config.Automations.Count);
        var profiles = active.Count == 0 ? "" : "\n" + Strings.Format("ActiveProfiles", string.Join(", ", active.Values.Select(p => p.Name)));
        var errors = _host.Store.LastResult?.HasErrors == true ? "\n⚠ " + Strings.Get("FileHasErrors") : "";
        HeaderStatus.Text = $"{state}{profiles}\n{(_host.Service.IsConnected ? Strings.Get("ServiceConnected") : Strings.Get("ServiceNotConnected"))}{errors}";
        PauseButton.Content = engine.IsPaused ? Strings.Get("Resume") : Strings.Get("PauseOneHour");

        if (SettingsPage.Visibility == Visibility.Visible)
            StatusDetails.Text = BuildStatus();
    }

    private string BuildStatus()
    {
        var sb = new StringBuilder();
        var load = _host.Store.LastResult;
        sb.AppendLine(Strings.Format("StatusUser", _host.User.QualifiedName, _host.SessionId));
        sb.AppendLine(Strings.Format("StatusService", _host.Service.IsConnected ? Strings.Get("Connected") : Strings.Get("NotConnectedLong")));
        sb.AppendLine(Strings.Format("StatusAppDetection", _host.AppDetection));
        sb.AppendLine(Strings.Format("StatusFocus", _host.Foreground.CurrentApp?.Name ?? "?"));
        sb.AppendLine(Strings.Format("StatusEngine", _host.Engine.IsPaused ? Strings.Get("Paused") : Strings.Get("Running"), _host.Engine.DryRun ? Strings.Get("DryRunSuffix") : ""));
        sb.AppendLine(Strings.Format("StatusFile", _host.Store.FilePath));
        if (load is not null)
            sb.AppendLine(Strings.Format("StatusLastLoad", load.HasErrors ? Strings.Format("ErrorsKeepPrevious", load.Errors.Count()) : "OK", load.Warnings.Count()));
        sb.AppendLine(Strings.Format("StatusMachineFile", Product.MachineConfigPath));
        sb.AppendLine(Strings.Format("StatusLogs", Product.AgentLogDirectory));
        if (_host.Engine.SuspendedAutomations.Count > 0)
            sb.AppendLine(Strings.Format("StatusSuspended", string.Join(", ", _host.Engine.SuspendedAutomations)));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ editing helpers

    /// <summary>Asks once whether the user accepts that the editor rewrites the file (comments are lost).</summary>
    private bool ConfirmRewrite()
    {
        if (_settings.RewriteNoticeAccepted)
            return true;
        var answer = MessageBox.Show(this, Strings.Get("RewriteNotice"), Product.Name, MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK)
            return false;
        _settings.RewriteNoticeAccepted = true;
        _settings.Save();
        return true;
    }

    private bool TryEdit(Action<ConfigDocument> change)
    {
        if (!ConfirmRewrite())
            return false;
        try
        {
            var result = _host.Edit(change);
            if (!result.HasErrors)
                return true;
            ShowError(string.Join("\n", result.Errors.Take(5).Select(i => i.Message)));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        return false;
    }

    private ConfigDocument? BeginEdit()
    {
        if (!ConfirmRewrite())
            return null;
        try
        {
            return _host.BeginEdit();
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
            return null;
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, Product.Name, MessageBoxButton.OK, MessageBoxImage.Warning);

    private string? SelectedAutomationId => (AutomationList.SelectedItem as AutomationRow)?.Id;

    private string? SelectedProfileId => (ProfileList.SelectedItem as ProfileRow)?.Id;

    // ------------------------------------------------------------------ automations

    private void OnNewAutomationClick(object sender, RoutedEventArgs e)
    {
        if (BeginEdit() is { } document)
            EditorWindow.ForAutomation(_host, document, null).ShowDialogWithOwner(this);
    }

    private void OnEditAutomationClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAutomationId is not { } id || BeginEdit() is not { } document)
            return;
        EditorWindow.ForAutomation(_host, document, document.Config.FindAutomation(id)).ShowDialogWithOwner(this);
    }

    private void OnDuplicateAutomationClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAutomationId is { } id)
            TryEdit(document => document.DuplicateAutomation(id));
    }

    private void OnDeleteAutomationClick(object sender, RoutedEventArgs e)
    {
        var ids = AutomationList.SelectedItems.OfType<AutomationRow>().Select(r => r.Id).ToList();
        if (ids.Count == 0)
            return;
        if (MessageBox.Show(this, Strings.Format("DeleteQuestion", ids.Count), Product.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        TryEdit(document => ids.ForEach(id => document.RemoveAutomation(id)));
    }

    private void OnMoveAutomationUpClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAutomationId is { } id)
            TryEdit(document => document.MoveAutomation(id, -1));
    }

    private void OnMoveAutomationDownClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAutomationId is { } id)
            TryEdit(document => document.MoveAutomation(id, 1));
    }

    private void OnToggleEnabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } element)
            return;
        var enabled = element is System.Windows.Controls.Primitives.ToggleButton { IsChecked: true };
        if (!TryEdit(document => document.SetEnabled(id, enabled)))
            Refresh();
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAutomationId is { } id)
            await _host.RunAutomationAsync(id, checkConditions: true);
    }

    private async void OnRunForcedClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAutomationId is { } id)
            await _host.RunAutomationAsync(id, checkConditions: false);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "YAML|*.yaml;*.yml|All files|*.*" };
        if (dialog.ShowDialog(this) != true)
            return;
        var result = ConfigLoader.Load(File.ReadAllText(dialog.FileName), ExecutionScope.User, AgentCatalog.Current);
        if (result.HasErrors)
        {
            ShowError(Strings.Get("ImportInvalid") + "\n\n" + string.Join("\n", result.Errors.Take(5).Select(i => i.ToString())));
            return;
        }
        MergeResult? merged = null;
        if (TryEdit(document => merged = ConfigMerge.Merge(document.Config, result.Config)) && merged is not null)
            MessageBox.Show(this, Strings.Format("Imported", merged.AutomationIds.Count, merged.ProfileIds.Count), Product.Name);
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var ids = AutomationList.SelectedItems.OfType<AutomationRow>().Select(r => r.Id).ToList();
        if (ids.Count == 0)
            ids = _host.Engine.Config.Automations.Select(a => a.Id).ToList();
        var dialog = new SaveFileDialog { Filter = "YAML|*.yaml", FileName = "automations-export.yaml" };
        if (dialog.ShowDialog(this) != true)
            return;
        var exported = ConfigMerge.Export(_host.Store.Current, ids, []);
        File.WriteAllText(dialog.FileName, YamlConfigWriter.Write(exported, ConfigDocument.Header));
    }

    private void OnEditFileClick(object sender, RoutedEventArgs e)
    {
        var find = SelectedAutomationId is { } id && AutomationsPage.Visibility == Visibility.Visible ? $"id: {id}" : null;
        new FileEditorWindow(_host, find) { Owner = this }.Show();
    }

    // ------------------------------------------------------------------ profiles

    private void OnNewProfileClick(object sender, RoutedEventArgs e)
    {
        if (BeginEdit() is { } document)
            EditorWindow.ForProfile(_host, document, null).ShowDialogWithOwner(this);
    }

    private void OnEditProfileClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileId is not { } id || BeginEdit() is not { } document)
            return;
        EditorWindow.ForProfile(_host, document, document.Config.FindProfile(id)).ShowDialogWithOwner(this);
    }

    private void OnDuplicateProfileClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileId is { } id)
            TryEdit(document => document.DuplicateProfile(id));
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileId is not { } id)
            return;
        if (MessageBox.Show(this, Strings.Format("DeleteQuestion", 1), Product.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            TryEdit(document => document.RemoveProfile(id));
    }

    private async void OnApplyProfileClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileId is { } id)
            await _host.ApplyProfileAsync(id);
    }

    private async void OnRevertProfileClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProfileId is { } id)
            await _host.RevertProfileAsync(id);
    }

    // ------------------------------------------------------------------ templates

    private void OnAddTemplateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || Templates.Find(id) is not { } template)
            return;
        MergeResult? merged = null;
        if (!TryEdit(document => merged = ConfigMerge.Merge(document.Config, template.Load())) || merged is null)
            return;
        ShowPage("automations");
        MessageBox.Show(this, Strings.Format("TemplateAdded", template.Title, merged.AutomationIds.Count, merged.ProfileIds.Count), Product.Name);
    }

    // ------------------------------------------------------------------ activity

    private void LoadPersonalActivity()
    {
        _activity.Clear();
        foreach (var entry in _host.Activity.Snapshot().Reverse())
            _activity.Add(ToRow(entry));
    }

    private void OnActivityAdded(object? sender, ActivityEntry entry) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (PersonalActivity.IsChecked != true)
                return;
            _activity.Insert(0, ToRow(entry));
            while (_activity.Count > 1000)
                _activity.RemoveAt(_activity.Count - 1);
        });

    private static ActivityRow ToRow(ActivityEntry entry)
    {
        var time = entry.Timestamp.ToString("HH:mm:ss", Strings.Culture);
        return entry.Level switch
        {
            ActivityLevel.Success => new ActivityRow("✓", SuccessBrush, time, entry.Message),
            ActivityLevel.Warning => new ActivityRow("!", WarningBrush, time, entry.Message),
            ActivityLevel.Error => new ActivityRow("✗", ErrorBrush, time, entry.Message),
            _ => new ActivityRow("•", InfoBrush, time, entry.Message),
        };
    }

    private async void OnActivitySourceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (MachineActivity.IsChecked == true)
        {
            _activity.Clear();
            var entries = await _host.Service.GetMachineActivityAsync();
            if (entries.Count == 0)
                _activity.Add(new ActivityRow("!", WarningBrush, "", _host.Service.IsConnected ? Strings.Get("NoMachineActivity") : Strings.Get("ServiceNotConnected")));
            foreach (var entry in entries.Reverse())
                _activity.Add(ToRow(entry));
        }
        else
        {
            LoadPersonalActivity();
        }
    }

    private void OnClearActivityClick(object sender, RoutedEventArgs e)
    {
        if (PersonalActivity.IsChecked == true)
            _host.Activity.Clear();
        _activity.Clear();
    }

    // ------------------------------------------------------------------ pause and settings

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_host.Engine.IsPaused)
            _host.Engine.Resume();
        else
            _host.Engine.Pause(TimeSpan.FromHours(1));
    }

    private void LoadSettingsPage()
    {
        _loadingSettings = true;
        LanguageBox.ItemsSource = Strings.Languages.Select(l => new ComboBoxItem { Content = l.Code == "auto" ? Strings.Get("FollowWindows") : l.Name, Tag = l.Code }).ToList();
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _settings.Language) ?? LanguageBox.Items[0];
        ThemeBox.ItemsSource = new[] { ("system", Strings.Get("FollowWindows")), ("light", Strings.Get("Light")), ("dark", Strings.Get("Dark")) }
            .Select(t => new ComboBoxItem { Content = t.Item2, Tag = t.Item1 }).ToList();
        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _settings.Theme) ?? ThemeBox.Items[0];
        DryRunSwitch.IsChecked = _host.Engine.DryRun;
        _loadingSettings = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code })
            return;
        _settings.Language = code;
        _settings.Save();
        RestartHint.Visibility = Visibility.Visible;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme })
            return;
        _settings.Theme = theme;
        _settings.Save();
        App.ApplyTheme(theme);
    }

    private void OnDryRunChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
            return;
        _host.Engine.DryRun = DryRunSwitch.IsChecked == true;
        Refresh();
    }

    // ------------------------------------------------------------------ updates

    private static readonly UpdateMode[] UpdateModes = [UpdateMode.AskFirst, UpdateMode.Automatic, UpdateMode.Notify, UpdateMode.Off];

    private void OnUpdateStatusChanged(object? sender, UpdateStatus status) => Dispatcher.InvokeAsync(RefreshUpdates);

    private void RefreshUpdates()
    {
        var status = _host.Updates;
        var wasLoading = _loadingSettings;
        _loadingSettings = true;
        if (UpdateModeBox.ItemsSource is null)
        {
            UpdateModeBox.ItemsSource = UpdateModes.Select(m => new ComboBoxItem { Content = Strings.Get("UpdateMode" + m), Tag = m }).ToList();
        }

        if (status is null)
        {
            UpdateStatusText.Text = Strings.Get("UpdatesNeedService");
            UpdateDetails.Text = "";
            UpdateModeBox.IsEnabled = BetaSwitch.IsEnabled = CheckUpdatesButton.IsEnabled = false;
            InstallUpdateButton.Visibility = WhatsNewButton.Visibility = Visibility.Collapsed;
            _loadingSettings = wasLoading;
            return;
        }

        var latest = status.Latest?.Version ?? "";
        UpdateStatusText.Text = status.State switch
        {
            UpdateState.Disabled => Strings.Get("UpdateStateDisabled"),
            UpdateState.Checking => Strings.Get("UpdateStateChecking"),
            UpdateState.UpToDate => Strings.Get("UpdateStateUpToDate"),
            UpdateState.Available => Strings.Format("UpdateStateAvailable", latest),
            UpdateState.Downloading => Strings.Format("UpdateStateDownloading", latest),
            UpdateState.Ready => Strings.Format("UpdateStateReady", latest),
            UpdateState.Installing => Strings.Format("UpdateStateInstalling", latest),
            UpdateState.Failed => Strings.Get("UpdateStateFailed"),
            _ => Strings.Get("UpdateStateUnknown"),
        };

        var details = new List<string> { Strings.Format("InstalledVersion", status.CurrentVersion) };
        if (status.LastChecked is { } checkedAt)
            details.Add(Strings.Format("LastChecked", checkedAt.ToLocalTime().ToString("g", Strings.Culture)));
        if (!string.IsNullOrWhiteSpace(status.Message))
            details.Add(status.Message);
        UpdateDetails.Text = string.Join("\n", details);

        InstallUpdateButton.Content = Strings.Format("InstallUpdate", latest);
        InstallUpdateButton.Visibility = status.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        WhatsNewButton.Visibility = status.Latest is not null ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = status.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing);

        UpdateModeBox.SelectedItem = UpdateModeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (UpdateMode)i.Tag == status.Settings.Mode);
        BetaSwitch.IsChecked = status.Settings.IncludePrereleases;
        UpdateModeBox.IsEnabled = BetaSwitch.IsEnabled = status.CanChangeSettings;
        UpdateLockedHint.Visibility = status.CanChangeSettings ? Visibility.Collapsed : Visibility.Visible;
        _loadingSettings = wasLoading;
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = Strings.Get("UpdateStateChecking");
        await _host.CheckForUpdatesAsync();
        RefreshUpdates();
    }

    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        InstallUpdateButton.IsEnabled = false;
        var error = await _host.InstallUpdateAsync();
        // While installing, this app is closed by the installer; the connection drop is not an error.
        if (!IsLoaded || _host.Updates?.State == UpdateState.Installing)
            return;
        InstallUpdateButton.IsEnabled = true;
        if (error is not null)
            ShowError(Strings.Format("UpdateError", error));
        RefreshUpdates();
    }

    private void OnWhatsNewClick(object sender, RoutedEventArgs e)
    {
        if (_host.Updates?.Latest?.PageUrl is { Length: > 0 } url && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }

    private async void OnUpdateSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || UpdateModeBox.SelectedItem is not ComboBoxItem { Tag: UpdateMode mode })
            return;
        var error = await _host.SetUpdateSettingsAsync(new UpdateSettings(mode, BetaSwitch.IsChecked == true));
        if (error is not null)
            ShowError(error);
        RefreshUpdates();
    }

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e) => TrayIcon.OpenFolder(Product.UserDataDirectory);

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e) => TrayIcon.OpenFolder(Product.AgentLogDirectory);

    private void OnDocsClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Product.DocumentationUrl) { UseShellExecute = true })?.Dispose();
}

/// <summary>Small window helpers.</summary>
internal static class WindowExtensions
{
    /// <summary>Shows a dialog owned by <paramref name="owner"/>.</summary>
    public static bool? ShowDialogWithOwner(this Window window, Window owner)
    {
        window.Owner = owner;
        return window.ShowDialog();
    }
}
