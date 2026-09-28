using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AutoSettings.Core;
using AutoSettings.Platform.Actions;
using Forms = System.Windows.Forms;

namespace AutoSettings.Agent;

/// <summary>The notification-area icon: quick controls and notifications.</summary>
public sealed class TrayIcon : INotifier, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Forms.NotifyIcon _icon;
    private AgentHost? _host;
    private Action? _open;
    private Action? _exit;

    public TrayIcon(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = Product.Name,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
    }

    /// <summary>Connects the menu to the agent.</summary>
    public void Attach(AgentHost host, Action open, Action exit)
    {
        _host = host;
        _open = open;
        _exit = exit;
        _icon.DoubleClick += (_, _) => open();
        _icon.BalloonTipClicked += (_, _) => open();
        _icon.ContextMenuStrip!.Opening += (_, _) => BuildMenu();
        host.StatusChanged += (_, _) => _dispatcher.InvokeAsync(UpdateTooltip);
        BuildMenu();
    }

    /// <inheritdoc />
    public void Notify(string title, string message) =>
        _dispatcher.InvokeAsync(() => _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.None));

    private void UpdateTooltip()
    {
        if (_host is null)
            return;
        var text = $"{Product.Name} — {(_host.Engine.IsPaused ? "paused" : $"{_host.Engine.Config.Automations.Count(a => a.Enabled)} automations on")}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void BuildMenu()
    {
        if (_host is not { } host)
            return;
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        var open = new Forms.ToolStripMenuItem($"Open {Product.Name}", null, (_, _) => _open?.Invoke());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());

        if (host.Engine.IsPaused)
        {
            var until = host.Engine.PausedUntil is { } time ? $" (paused until {time:t})" : " (paused)";
            menu.Items.Add(new Forms.ToolStripMenuItem("Resume automations" + until, null, (_, _) => host.Engine.Resume()));
        }
        else
        {
            var pause = new Forms.ToolStripMenuItem("Pause automations");
            pause.DropDownItems.Add("For 15 minutes", null, (_, _) => host.Engine.Pause(TimeSpan.FromMinutes(15)));
            pause.DropDownItems.Add("For 1 hour", null, (_, _) => host.Engine.Pause(TimeSpan.FromHours(1)));
            pause.DropDownItems.Add("Until I resume", null, (_, _) => host.Engine.Pause());
            menu.Items.Add(pause);
        }

        var profiles = new Forms.ToolStripMenuItem("Profiles");
        foreach (var profile in host.Engine.Config.Profiles)
        {
            var active = host.Engine.IsProfileActive(profile.Id);
            var item = new Forms.ToolStripMenuItem(profile.DisplayName) { Checked = active };
            item.Click += (_, _) => _ = active ? host.RevertProfileAsync(profile.Id) : host.ApplyProfileAsync(profile.Id);
            profiles.DropDownItems.Add(item);
        }
        if (profiles.DropDownItems.Count == 0)
            profiles.DropDownItems.Add(new Forms.ToolStripMenuItem("No profiles yet") { Enabled = false });
        menu.Items.Add(profiles);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Edit automations file", null, (_, _) => OpenInEditor(host.Store.FilePath));
        menu.Items.Add("Reload automations", null, (_, _) => host.Store.Load());
        menu.Items.Add("Open log folder", null, (_, _) => OpenFolder(Product.AgentLogDirectory));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _exit?.Invoke());
    }

    /// <summary>Opens a file in the user's editor for its type, or Notepad.</summary>
    public static void OpenInEditor(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>Opens a folder in Explorer.</summary>
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon)
                return icon;
        }
        catch (Exception)
        {
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
