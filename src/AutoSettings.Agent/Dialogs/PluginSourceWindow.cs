using System.Windows;
using System.Windows.Controls;
using AutoSettings.Agent.Localization;
using AutoSettings.Core.Plugins;

namespace AutoSettings.Agent.Dialogs;

/// <summary>Asks for the GitHub repository to install a plugin from.</summary>
public sealed class PluginSourceWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Wpf.Ui.Controls.TextBox _repository;
    private readonly CheckBox _betas;
    private readonly TextBlock _error;

    public PluginSourceWindow()
    {
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendsContentIntoTitleBar = true;

        _repository = new Wpf.Ui.Controls.TextBox { PlaceholderText = "owner/name", Margin = new Thickness(0, 6, 0, 8) };
        _betas = new CheckBox { Content = Strings.Get("IncludeBetas") };
        _error = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        var ok = new Wpf.Ui.Controls.Button { Content = Strings.Get("Install"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = Strings.Get("Cancel"), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var body = new StackPanel { Margin = new Thickness(20, 8, 20, 20) };
        body.Children.Add(new TextBlock { Text = Strings.Get("GitHubRepository"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_repository);
        body.Children.Add(_betas);
        body.Children.Add(_error);
        body.Children.Add(buttons);

        var root = new StackPanel();
        root.Children.Add(new Wpf.Ui.Controls.TitleBar { Title = Strings.Get("InstallFromGitHub") });
        root.Children.Add(body);
        Content = root;
        Loaded += (_, _) => _repository.Focus();
    }

    /// <summary>The repository, <c>owner/name</c>, after OK.</summary>
    public string? Repository { get; private set; }

    /// <summary>Whether pre-releases may be installed.</summary>
    public bool IncludeBetas => _betas.IsChecked == true;

    private void Accept()
    {
        if (!PluginReleases.TryParseRepository(_repository.Text, out var repository))
        {
            _error.Text = Strings.Get("GitHubRepository");
            return;
        }
        Repository = repository;
        DialogResult = true;
        Close();
    }
}
