using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoSettings.Agent.Localization;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Config;
using AutoSettings.Core.Editing;

namespace AutoSettings.Agent.Dialogs;

/// <summary>Edits the whole automations.yaml as text, keeping comments and formatting.</summary>
public partial class FileEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AgentHost _host;
    private bool _dirty;

    public FileEditorWindow(AgentHost host, string? findText = null)
    {
        _host = host;
        InitializeComponent();
        WindowTitleBar.Title = host.Store.FilePath;
        Yaml.DocumentKind = YamlDocumentKind.File;
        Yaml.Scope = ExecutionScope.User;
        Yaml.Changed += (_, _) =>
        {
            _dirty = true;
            StatusText.Text = Strings.Get("UnsavedChanges");
        };
        Yaml.TextChangedDebounced += (_, _) => Validate();
        Load();
        if (findText is not null)
            Loaded += (_, _) => Yaml.Find(findText);
        Closing += (_, e) =>
        {
            if (_dirty && MessageBox.Show(this, Strings.Get("DiscardQuestion"), Core.Product.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    private void Load()
    {
        Yaml.Text = _host.Store.ReadText();
        _dirty = false;
        StatusText.Text = "";
        Validate();
    }

    private ConfigLoadResult Validate()
    {
        var result = ConfigLoader.Load(Yaml.Text, ExecutionScope.User);
        Yaml.SetIssues(result.Issues);
        IssueList.ItemsSource = result.Issues.Select(i => new ListBoxItem
        {
            Content = i.ToString(),
            Tag = i,
            Foreground = i.Severity == IssueSeverity.Error ? System.Windows.Media.Brushes.IndianRed : System.Windows.Media.Brushes.DarkOrange,
        }).ToList();
        IssueList.Visibility = result.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return result;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _host.Store.Save(Yaml.Text);
            Yaml.SetIssues(result.Issues);
            if (result.HasErrors)
            {
                Validate();
                StatusText.Text = Strings.Get("NotSavedFixErrors");
                return;
            }
            _dirty = false;
            StatusText.Text = Strings.Format("SavedAt", DateTime.Now.ToString("t", Strings.Culture));
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.Get("NotSaved") + " " + ex.Message;
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => Load();

    private void OnExternalClick(object sender, RoutedEventArgs e) => TrayIcon.OpenInEditor(_host.Store.FilePath);

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueList.SelectedItem is ListBoxItem { Tag: ConfigIssue { Location: { } location } })
            Yaml.GoTo(location.Line, location.Column);
    }
}
