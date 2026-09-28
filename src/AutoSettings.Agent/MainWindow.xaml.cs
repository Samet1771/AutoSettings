using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutoSettings.Core;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Engine;

namespace AutoSettings.Agent;

/// <summary>One row in the automation list.</summary>
public sealed record AutomationRow(string Id, string Enabled, string Name, string When, string If, string Then);

/// <summary>One row in the profile list.</summary>
public sealed record ProfileRow(string Id, string Status, string Name, int Priority, string Actions);

/// <summary>One row in the activity list.</summary>
public sealed record ActivityRow(string Icon, Brush Brush, string Time, string Message);

/// <summary>The main window.</summary>
public partial class MainWindow : Window
{
    private static readonly Brush InfoBrush = Brushes.SteelBlue;
    private static readonly Brush SuccessBrush = Brushes.SeaGreen;
    private static readonly Brush WarningBrush = Brushes.DarkOrange;
    private static readonly Brush ErrorBrush = Brushes.Firebrick;

    private readonly AgentHost _host;
    private readonly ObservableCollection<ActivityRow> _activity = [];
    private bool _yamlDirty;
    private bool _loadingYaml;

    public MainWindow(AgentHost host)
    {
        _host = host;
        InitializeComponent();

        ActivityList.ItemsSource = _activity;
        DryRunCheck.IsChecked = host.Engine.DryRun;

        host.Activity.EntryAdded += OnActivityAdded;
        host.StatusChanged += OnStatusChanged;
        host.Store.Loaded += OnConfigLoaded;
        Closed += (_, _) =>
        {
            host.Activity.EntryAdded -= OnActivityAdded;
            host.StatusChanged -= OnStatusChanged;
            host.Store.Loaded -= OnConfigLoaded;
        };

        LoadPersonalActivity();
        LoadYaml();
        Refresh();
    }

    // ------------------------------------------------------------------ refresh

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    private void OnConfigLoaded(object? sender, ConfigLoadResult result) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!_yamlDirty)
                LoadYaml();
            Refresh();
        });

    private void Refresh()
    {
        var engine = _host.Engine;
        var config = engine.Config;

        var selectedAutomation = (AutomationList.SelectedItem as AutomationRow)?.Id;
        AutomationList.ItemsSource = config.Automations.Select(a => new AutomationRow(
            a.Id,
            a.Enabled ? (engine.SuspendedAutomations.Contains(a.Id) ? "⏸" : "✓") : "—",
            a.DisplayName,
            ComponentSummary.DescribeAll(ComponentKind.Trigger, a.Triggers, " or "),
            a.Conditions.Count == 0 ? "always" : ComponentSummary.DescribeAll(ComponentKind.Condition, a.Conditions, " and "),
            ComponentSummary.DescribeAll(ComponentKind.Action, a.Actions, " → "))).ToList();
        AutomationList.SelectedItem = (AutomationList.ItemsSource as List<AutomationRow>)?.FirstOrDefault(r => r.Id == selectedAutomation);

        var active = engine.ActiveProfiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var selectedProfile = (ProfileList.SelectedItem as ProfileRow)?.Id;
        ProfileList.ItemsSource = config.Profiles.Select(p => new ProfileRow(
            p.Id,
            active.TryGetValue(p.Id, out var info)
                ? $"● Active{(info.RevertsWhen is null ? "" : $", reverts {info.RevertsWhen}")}"
                : "Off",
            p.DisplayName,
            p.Priority,
            ComponentSummary.DescribeAll(ComponentKind.Action, p.Actions, "; "))).ToList();
        ProfileList.SelectedItem = (ProfileList.ItemsSource as List<ProfileRow>)?.FirstOrDefault(r => r.Id == selectedProfile);

        var enabled = config.Automations.Count(a => a.Enabled);
        var paused = engine.IsPaused
            ? engine.PausedUntil is { } until ? $"Paused until {until:t}" : "Paused"
            : $"{enabled} of {config.Automations.Count} automations on";
        var profiles = active.Count == 0 ? "" : $" · Active profiles: {string.Join(", ", active.Values.Select(p => p.Name))}";
        HeaderStatus.Text = $"{paused}{profiles} · {(_host.Service.IsConnected ? "Service connected" : "Service not connected")}";
        PauseButton.Content = engine.IsPaused ? "Resume" : "Pause";

        StatusDetails.Text = BuildStatus();
    }

    private string BuildStatus()
    {
        var sb = new StringBuilder();
        var load = _host.Store.LastResult;
        sb.AppendLine($"Signed in as: {_host.User.QualifiedName} (session {_host.SessionId})");
        sb.AppendLine($"Service: {(_host.Service.IsConnected ? "connected" : "not connected — sign-in and lock/unlock detection need the service")}");
        sb.AppendLine($"App start/close detection: {_host.AppDetection}");
        sb.AppendLine($"Focus detection: {_host.Foreground.Name}, current app: {_host.Foreground.CurrentApp?.Name ?? "unknown"}");
        sb.AppendLine($"Automations: {(_host.Engine.IsPaused ? "paused" : "running")}{(_host.Engine.DryRun ? " (dry run)" : "")}");
        sb.AppendLine($"Configuration file: {_host.Store.FilePath}");
        if (load is not null)
            sb.AppendLine($"Last load: {(load.HasErrors ? $"{load.Errors.Count()} error(s) — the previous version is still running" : "OK")}{(load.Warnings.Any() ? $", {load.Warnings.Count()} warning(s)" : "")}");
        sb.AppendLine($"Machine automations: {Product.MachineConfigPath} (editable by administrators)");
        sb.AppendLine($"Logs: {Product.AgentLogDirectory}");
        var suspended = _host.Engine.SuspendedAutomations;
        if (suspended.Count > 0)
            sb.AppendLine($"Suspended by the loop guard: {string.Join(", ", suspended)} (save the file or restart to resume)");
        return sb.ToString();
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

    private static ActivityRow ToRow(ActivityEntry entry) => entry.Level switch
    {
        ActivityLevel.Success => new ActivityRow("✓", SuccessBrush, entry.Timestamp.ToString("HH:mm:ss"), entry.Message),
        ActivityLevel.Warning => new ActivityRow("!", WarningBrush, entry.Timestamp.ToString("HH:mm:ss"), entry.Message),
        ActivityLevel.Error => new ActivityRow("✗", ErrorBrush, entry.Timestamp.ToString("HH:mm:ss"), entry.Message),
        _ => new ActivityRow("•", InfoBrush, entry.Timestamp.ToString("HH:mm:ss"), entry.Message),
    };

    private async void OnActivitySourceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && sender == PersonalActivity)
            return;
        if (MachineActivity.IsChecked == true)
        {
            _activity.Clear();
            var entries = await _host.Service.GetMachineActivityAsync();
            if (entries.Count == 0)
                _activity.Add(new ActivityRow("!", WarningBrush, "", _host.Service.IsConnected ? "No machine activity yet." : "The service is not connected."));
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

    // ------------------------------------------------------------------ automations & profiles

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (AutomationList.SelectedItem is AutomationRow row)
            await _host.RunAutomationAsync(row.Id, checkConditions: true);
    }

    private async void OnRunForcedClick(object sender, RoutedEventArgs e)
    {
        if (AutomationList.SelectedItem is AutomationRow row)
            await _host.RunAutomationAsync(row.Id, checkConditions: false);
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        Tabs.SelectedIndex = 3;
        if (AutomationList.SelectedItem is AutomationRow row)
            SelectText($"id: {row.Id}");
    }

    private async void OnApplyProfileClick(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ProfileRow row)
            await _host.ApplyProfileAsync(row.Id);
    }

    private async void OnRevertProfileClick(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is ProfileRow row)
            await _host.RevertProfileAsync(row.Id);
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_host.Engine.IsPaused)
            _host.Engine.Resume();
        else
            _host.Engine.Pause(TimeSpan.FromHours(1));
    }

    private void OnDryRunChanged(object sender, RoutedEventArgs e)
    {
        _host.Engine.DryRun = DryRunCheck.IsChecked == true;
        Refresh();
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && Tabs.SelectedIndex == 4)
            StatusDetails.Text = BuildStatus();
    }

    // ------------------------------------------------------------------ YAML editor

    private void LoadYaml()
    {
        _loadingYaml = true;
        YamlEditor.Text = _host.Store.ReadText();
        _loadingYaml = false;
        _yamlDirty = false;
        ShowIssues(_host.Store.LastResult?.Issues ?? Array.Empty<ConfigIssue>());
        YamlStatus.Text = "";
    }

    private void OnYamlChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingYaml)
            return;
        _yamlDirty = true;
        YamlStatus.Text = "Unsaved changes";
    }

    private void OnValidateYamlClick(object sender, RoutedEventArgs e)
    {
        var result = ConfigLoader.Load(YamlEditor.Text, ExecutionScope.User);
        ShowIssues(result.Issues);
        YamlStatus.Text = result.HasErrors ? "Fix the errors before saving." : "No errors.";
    }

    private void OnSaveYamlClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _host.Store.Save(YamlEditor.Text);
            ShowIssues(result.Issues);
            if (result.HasErrors)
            {
                YamlStatus.Text = "Not saved: fix the errors first.";
                return;
            }
            _yamlDirty = false;
            YamlStatus.Text = $"Saved at {DateTime.Now:t}.";
        }
        catch (Exception ex)
        {
            YamlStatus.Text = "Not saved: " + ex.Message;
        }
    }

    private void OnReloadYamlClick(object sender, RoutedEventArgs e) => LoadYaml();

    private void OnExternalEditorClick(object sender, RoutedEventArgs e) => TrayIcon.OpenInEditor(_host.Store.FilePath);

    private void ShowIssues(IEnumerable<ConfigIssue> issues)
    {
        IssueList.ItemsSource = issues.Select(i => new ListBoxItem
        {
            Content = i.ToString(),
            Tag = i,
            Foreground = i.Severity == IssueSeverity.Error ? ErrorBrush : WarningBrush,
        }).ToList();
    }

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueList.SelectedItem is ListBoxItem { Tag: ConfigIssue { Location: { } location } })
            GoToLine(location.Line, location.Column);
    }

    private void GoToLine(int line, int column)
    {
        var index = 0;
        for (var i = 1; i < line && index >= 0; i++)
        {
            index = YamlEditor.Text.IndexOf('\n', index);
            if (index >= 0)
                index++;
        }
        if (index < 0)
            return;
        YamlEditor.Focus();
        YamlEditor.Select(Math.Min(index + Math.Max(0, column - 1), YamlEditor.Text.Length), 0);
        YamlEditor.ScrollToLine(Math.Max(0, line - 1));
    }

    private void SelectText(string text)
    {
        var index = YamlEditor.Text.IndexOf(text, StringComparison.Ordinal);
        if (index < 0)
            return;
        YamlEditor.Focus();
        YamlEditor.Select(index, text.Length);
        YamlEditor.ScrollToLine(YamlEditor.GetLineIndexFromCharacterIndex(index));
    }

    // ------------------------------------------------------------------ status tab

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e) => TrayIcon.OpenFolder(Product.UserDataDirectory);

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e) => TrayIcon.OpenFolder(Product.AgentLogDirectory);

    private void OnDocsClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Product.DocumentationUrl) { UseShellExecute = true })?.Dispose();
}
