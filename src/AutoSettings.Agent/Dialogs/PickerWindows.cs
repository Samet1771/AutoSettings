using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoSettings.Agent.Localization;
using AutoSettings.Platform.Monitoring;

namespace AutoSettings.Agent.Dialogs;

/// <summary>An app in the picker.</summary>
public sealed class AppChoice
{
    public required string Name { get; init; }

    public string? Path { get; init; }

    public required string Source { get; init; }

    public ImageSource? Icon { get; set; }
}

/// <summary>Base for the small picker dialogs: title bar, search box, list, OK/Cancel.</summary>
public abstract class PickerWindowBase : Wpf.Ui.Controls.FluentWindow
{
    protected PickerWindowBase(string title)
    {
        Width = 640;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendsContentIntoTitleBar = true;

        Search = new Wpf.Ui.Controls.TextBox { PlaceholderText = Strings.Get("Search"), Margin = new Thickness(16, 8, 16, 8) };
        List = new ListView { Margin = new Thickness(16, 0, 16, 0) };
        Extra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 8, 16, 0) };

        var ok = new Wpf.Ui.Controls.Button { Content = Strings.Get("Select"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = Strings.Get("Cancel"), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var grid = new Grid();
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        var titleBar = new Wpf.Ui.Controls.TitleBar { Title = title };
        AddRow(grid, titleBar, 0);
        AddRow(grid, Search, 1);
        AddRow(grid, List, 2);
        AddRow(grid, Extra, 3);
        AddRow(grid, buttons, 4);
        Content = grid;

        List.MouseDoubleClick += (_, _) => Accept();
        Search.TextChanged += (_, _) => CollectionViewSource.GetDefaultView(List.ItemsSource)?.Refresh();
    }

    protected Wpf.Ui.Controls.TextBox Search { get; }

    protected ListView List { get; }

    protected StackPanel Extra { get; }

    protected abstract bool TryAccept();

    protected void UseItems<T>(ObservableCollection<T> items, Func<T, string> searchText)
    {
        List.ItemsSource = items;
        CollectionViewSource.GetDefaultView(items).Filter = o =>
            string.IsNullOrWhiteSpace(Search.Text) || searchText((T)o).Contains(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void Accept()
    {
        if (TryAccept())
        {
            DialogResult = true;
            Close();
        }
    }

    private static void AddRow(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}

/// <summary>Picks an app from the running apps and the Start menu.</summary>
public sealed class AppPickerWindow : PickerWindowBase
{
    private readonly ObservableCollection<AppChoice> _apps = [];
    private readonly CheckBox _fullPath;

    public AppPickerWindow() : base(Strings.Get("PickAppTitle"))
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "", Width = 40, CellTemplate = IconTemplate() });
        view.Columns.Add(new GridViewColumn { Header = Strings.Get("App"), Width = 180, DisplayMemberBinding = new Binding(nameof(AppChoice.Name)) });
        view.Columns.Add(new GridViewColumn { Header = Strings.Get("Found"), Width = 90, DisplayMemberBinding = new Binding(nameof(AppChoice.Source)) });
        view.Columns.Add(new GridViewColumn { Header = Strings.Get("PathColumn"), Width = 280, DisplayMemberBinding = new Binding(nameof(AppChoice.Path)) });
        List.View = view;
        UseItems(_apps, a => a.Name + " " + a.Path);

        _fullPath = new CheckBox { Content = Strings.Get("UseFullPath") };
        Extra.Children.Add(_fullPath);
        Loaded += async (_, _) => await LoadAppsAsync();
    }

    /// <summary>The exe name (or full path) chosen.</summary>
    public string? SelectedPattern { get; private set; }

    protected override bool TryAccept()
    {
        if (List.SelectedItem is not AppChoice app)
            return false;
        SelectedPattern = _fullPath.IsChecked == true && app.Path is not null ? app.Path : app.Name;
        return true;
    }

    private async Task LoadAppsAsync()
    {
        var found = await Task.Run(FindApps);
        foreach (var app in found)
            _apps.Add(app);
        await Task.Run(() =>
        {
            foreach (var app in found.Where(a => a.Path is not null))
            {
                var icon = LoadIcon(app.Path!);
                if (icon is not null)
                    Dispatcher.Invoke(() => app.Icon = icon);
            }
        });
        List.Items.Refresh();
    }

    private static List<AppChoice> FindApps()
    {
        var result = new Dictionary<string, AppChoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, process) in ProcessQuery.Snapshot(Sessions.CurrentSessionId()))
        {
            if (process.Path is null || process.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                continue;
            result.TryAdd(process.Name, new AppChoice { Name = process.Name, Path = process.Path, Source = Strings.Get("Running") });
        }
        foreach (var target in StartMenuTargets())
        {
            var name = Path.GetFileName(target);
            result.TryAdd(name, new AppChoice { Name = name, Path = target, Source = Strings.Get("StartMenu") });
        }
        return result.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> StartMenuTargets()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            yield break;
        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var link in links)
            {
                string? target = null;
                try
                {
                    target = (string)shell.CreateShortcut(link).TargetPath;
                }
                catch (Exception)
                {
                }
                if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    yield return target;
            }
        }
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DataTemplate IconTemplate()
    {
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(AppChoice.Icon)));
        image.SetValue(WidthProperty, 20.0);
        image.SetValue(HeightProperty, 20.0);
        return new DataTemplate { VisualTree = image };
    }
}

/// <summary>A user in the picker.</summary>
/// <param name="Name">User name.</param>
/// <param name="QualifiedName">DOMAIN\name.</param>
/// <param name="Source">Where it was found.</param>
public sealed record UserChoice(string Name, string QualifiedName, string Source);

/// <summary>Picks a user account of this computer.</summary>
public sealed class UserPickerWindow : PickerWindowBase
{
    private readonly ObservableCollection<UserChoice> _users = [];
    private readonly CheckBox _qualified;

    public UserPickerWindow() : base(Strings.Get("PickUserTitle"))
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = Strings.Get("User"), Width = 200, DisplayMemberBinding = new Binding(nameof(UserChoice.Name)) });
        view.Columns.Add(new GridViewColumn { Header = Strings.Get("Account"), Width = 240, DisplayMemberBinding = new Binding(nameof(UserChoice.QualifiedName)) });
        view.Columns.Add(new GridViewColumn { Header = Strings.Get("Found"), Width = 120, DisplayMemberBinding = new Binding(nameof(UserChoice.Source)) });
        List.View = view;
        UseItems(_users, u => u.QualifiedName);

        _qualified = new CheckBox { Content = Strings.Get("UseDomainName") };
        Extra.Children.Add(_qualified);
        Loaded += async (_, _) =>
        {
            foreach (var user in await Task.Run(FindUsers))
                _users.Add(user);
        };
    }

    /// <summary>The chosen user name.</summary>
    public string? SelectedUser { get; private set; }

    protected override bool TryAccept()
    {
        if (List.SelectedItem is not UserChoice user)
            return false;
        SelectedUser = _qualified.IsChecked == true ? user.QualifiedName : user.Name;
        return true;
    }

    private static List<UserChoice> FindUsers()
    {
        var result = new Dictionary<string, UserChoice>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in Sessions.List())
        {
            if (session.User is { } user)
                result.TryAdd(user.QualifiedName, new UserChoice(user.Name, user.QualifiedName, Strings.Get("SignedIn")));
        }
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Domain FROM Win32_UserAccount WHERE LocalAccount = True AND Disabled = False");
            foreach (ManagementObject account in searcher.Get())
            {
                using (account)
                {
                    var name = account["Name"] as string;
                    var domain = account["Domain"] as string;
                    if (!string.IsNullOrEmpty(name))
                        result.TryAdd($"{domain}\\{name}", new UserChoice(name, $"{domain}\\{name}", Strings.Get("LocalAccount")));
                }
            }
        }
        catch (ManagementException)
        {
        }
        return result.Values.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
