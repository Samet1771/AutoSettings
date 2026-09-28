using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoSettings.Agent.Localization;
using AutoSettings.Agent.ViewModels;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Editing;
using AutoSettings.Core.Model;
using Microsoft.Win32;

namespace AutoSettings.Agent.Dialogs;

/// <summary>
/// Edits one automation or profile in two synchronized views: a visual form built from the catalog,
/// and YAML with autocomplete. Like Home Assistant, you can only leave the YAML view when it is valid.
/// </summary>
public partial class EditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AgentHost _host;
    private readonly ConfigDocument _document;
    private readonly ItemEditorViewModel _model;
    private readonly string? _originalId;
    private bool _switchingTabs;
    private int _previousTab;

    private EditorWindow(AgentHost host, ConfigDocument document, bool isProfile, string? originalId)
    {
        _host = host;
        _document = document;
        _originalId = originalId;
        _model = new ItemEditorViewModel(isProfile, document.Scope);
        InitializeComponent();

        DataContext = _model;
        Yaml.DocumentKind = isProfile ? YamlDocumentKind.Profile : YamlDocumentKind.Automation;
        Yaml.Scope = document.Scope;
        Yaml.KnownProfiles = () => _document.Config.Profiles.Select(p => p.Id);
        Yaml.TextChangedDebounced += (_, _) => ValidateYaml();
        _model.Modified += (_, _) => ValidateVisual();
        TestButton.Visibility = isProfile ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>True when the user saved.</summary>
    public bool Saved { get; private set; }

    /// <summary>Opens the editor for an existing automation, or a new one when <paramref name="automation"/> is null.</summary>
    public static EditorWindow ForAutomation(AgentHost host, ConfigDocument document, Automation? automation)
    {
        var window = new EditorWindow(host, document, isProfile: false, automation?.Id);
        var item = automation ?? new Automation { Id = document.UniqueAutomationId("new-automation"), Name = Strings.Get("NewAutomationName") };
        window._model.Load(item);
        window.WindowTitleBar.Title = Strings.Format("EditAutomationTitle", item.DisplayName);
        window.ValidateVisual();
        return window;
    }

    /// <summary>Opens the editor for an existing profile, or a new one.</summary>
    public static EditorWindow ForProfile(AgentHost host, ConfigDocument document, Profile? profile)
    {
        var window = new EditorWindow(host, document, isProfile: true, profile?.Id);
        var item = profile ?? new Profile { Id = document.UniqueProfileId("new-profile"), Name = Strings.Get("NewProfileName") };
        window._model.Load(item);
        window.WindowTitleBar.Title = Strings.Format("EditProfileTitle", item.DisplayName);
        window.ValidateVisual();
        return window;
    }

    // ------------------------------------------------------------------ switching views

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingTabs || e.Source != Tabs || Tabs.SelectedIndex == _previousTab)
            return;

        if (Tabs.SelectedIndex == 1)
        {
            Yaml.Text = _model.IsProfile
                ? YamlConfigWriter.WriteProfile(_model.ToProfile())
                : YamlConfigWriter.WriteAutomation(_model.ToAutomation());
            ValidateYaml();
        }
        else if (!LoadFromYaml())
        {
            // Stay on the YAML view until it is valid.
            _switchingTabs = true;
            Tabs.SelectedIndex = 1;
            _switchingTabs = false;
            StatusText.Text = Strings.Get("FixYamlFirst");
            return;
        }
        _previousTab = Tabs.SelectedIndex;
    }

    private bool LoadFromYaml()
    {
        if (_model.IsProfile)
        {
            var (profile, issues) = _document.ParseProfile(Yaml.Text, _originalId);
            ShowIssues(issues);
            if (profile is null || issues.Any(i => i.Severity == IssueSeverity.Error))
                return false;
            _model.Load(profile);
        }
        else
        {
            var (automation, issues) = _document.ParseAutomation(Yaml.Text, _originalId);
            ShowIssues(issues);
            if (automation is null || issues.Any(i => i.Severity == IssueSeverity.Error))
                return false;
            _model.Load(automation);
        }
        return true;
    }

    // ------------------------------------------------------------------ validation

    private IReadOnlyList<ConfigIssue> ValidateVisual()
    {
        if (Tabs.SelectedIndex == 1)
            return [];
        var yaml = _model.IsProfile ? YamlConfigWriter.WriteProfile(_model.ToProfile()) : YamlConfigWriter.WriteAutomation(_model.ToAutomation());
        var issues = _model.IsProfile ? _document.ParseProfile(yaml, _originalId).Issues : _document.ParseAutomation(yaml, _originalId).Issues;
        // Locations refer to generated YAML, so only the messages are shown in the visual view.
        ShowIssues(issues.Select(i => i with { Location = null }).ToList());
        return issues;
    }

    private IReadOnlyList<ConfigIssue> ValidateYaml()
    {
        var issues = _model.IsProfile ? _document.ParseProfile(Yaml.Text, _originalId).Issues : _document.ParseAutomation(Yaml.Text, _originalId).Issues;
        Yaml.SetIssues(issues);
        ShowIssues(issues);
        return issues;
    }

    private void ShowIssues(IReadOnlyList<ConfigIssue> issues)
    {
        IssueList.ItemsSource = issues.Select(i => new ListBoxItem
        {
            Content = i.ToString(),
            Tag = i,
            Foreground = i.Severity == IssueSeverity.Error ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.DarkOrange,
        }).ToList();
        IssueList.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = issues.Any(i => i.Severity == IssueSeverity.Error) ? Strings.Get("HasErrors") : "";
    }

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tabs.SelectedIndex == 1 && IssueList.SelectedItem is ListBoxItem { Tag: ConfigIssue { Location: { } location } })
            Yaml.GoTo(location.Line, location.Column);
    }

    // ------------------------------------------------------------------ buttons

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedIndex == 1 && !LoadFromYaml())
            return;
        var issues = ValidateVisualAlways();
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            StatusText.Text = Strings.Get("NotSavedFixErrors");
            return;
        }

        try
        {
            ConfigLoadResult result;
            if (_model.IsProfile)
            {
                var profile = _model.ToProfile();
                result = _host.Edit(document => document.ReplaceProfile(_originalId ?? profile.Id, profile));
            }
            else
            {
                var automation = _model.ToAutomation();
                result = _host.Edit(document => document.ReplaceAutomation(_originalId ?? automation.Id, automation));
            }
            if (result.HasErrors)
            {
                ShowIssues(result.Issues.Select(i => i with { Location = null }).ToList());
                StatusText.Text = Strings.Get("NotSavedFixErrors");
                return;
            }
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.Get("NotSaved") + " " + ex.Message;
        }
    }

    private IReadOnlyList<ConfigIssue> ValidateVisualAlways()
    {
        var yaml = _model.IsProfile ? YamlConfigWriter.WriteProfile(_model.ToProfile()) : YamlConfigWriter.WriteAutomation(_model.ToAutomation());
        var issues = _model.IsProfile ? _document.ParseProfile(yaml, _originalId).Issues : _document.ParseAutomation(yaml, _originalId).Issues;
        ShowIssues(issues.Select(i => i with { Location = null }).ToList());
        return issues;
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedIndex == 1 && !LoadFromYaml())
            return;
        StatusText.Text = Strings.Get("TestStarted");
        await _host.TestActionsAsync(_model.ToAutomation());
        StatusText.Text = Strings.Get("TestFinished");
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ add menus and pickers

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ComponentListViewModel list } element)
            return;
        var menu = new ContextMenu { PlacementTarget = element };
        foreach (var group in list.AddMenu)
        {
            var category = new System.Windows.Controls.MenuItem { Header = Strings.Category(group.Key) };
            foreach (var descriptor in group)
            {
                var item = new System.Windows.Controls.MenuItem { Header = Strings.Title(descriptor), ToolTip = descriptor.Description };
                item.Click += (_, _) => list.AddNew(descriptor);
                category.Items.Add(item);
            }
            menu.Items.Add(category);
        }
        menu.IsOpen = true;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FieldViewModel field })
            return;
        if (field.Name.Contains("directory", StringComparison.OrdinalIgnoreCase))
        {
            var folder = new OpenFolderDialog();
            if (folder.ShowDialog(this) == true)
                field.Text = folder.FolderName;
            return;
        }
        var file = new OpenFileDialog
        {
            Filter = field.Name == "path" && _model.Actions.Items.Any(a => a.Descriptor?.Type == "wallpaper.set")
                ? "Images|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*"
                : "Programs|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All files|*.*",
        };
        if (file.ShowDialog(this) == true)
            field.Text = file.FileName;
    }

    private void OnPickAppClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FieldViewModel field })
            return;
        var picker = new AppPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPattern is { } pattern)
            field.AppendItem(pattern);
    }

    private void OnPickUserClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FieldViewModel field })
            return;
        var picker = new UserPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedUser is { } user)
            field.AppendItem(user);
    }
}
