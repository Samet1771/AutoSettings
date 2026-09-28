using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;
using Microsoft.Win32;

namespace AutoSettings.Platform.Actions;

/// <summary><c>theme.mode</c>: light/dark mode for apps and/or the system.</summary>
public sealed class ThemeModeAction : SyncRevertibleActionHandler
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsValue = "AppsUseLightTheme";
    private const string SystemValue = "SystemUsesLightTheme";

    /// <inheritdoc />
    public override string Type => "theme.mode";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var target = action.GetString("target") ?? "both";
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        var light = action.GetString("mode") switch
        {
            "light" => true,
            "dark" => false,
            _ => Reg.GetDword(key, target == "system" ? SystemValue : AppsValue, 1) == 0,
        };
        if (target is "both" or "apps")
            Reg.SetDword(key, AppsValue, light ? 1 : 0);
        if (target is "both" or "system")
            Reg.SetDword(key, SystemValue, light ? 1 : 0);
        User32.BroadcastSettingChange("ImmersiveColorSet");
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: false);
        return $"{Reg.GetDword(key, AppsValue, 1)},{Reg.GetDword(key, SystemValue, 1)}";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? "1,1").Split(',');
        var target = action.GetString("target") ?? "both";
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (target is "both" or "apps")
            Reg.SetDword(key, AppsValue, int.Parse(parts[0]));
        if (target is "both" or "system")
            Reg.SetDword(key, SystemValue, int.Parse(parts[1]));
        User32.BroadcastSettingChange("ImmersiveColorSet");
    }
}

/// <summary><c>theme.transparency</c>: transparency effects.</summary>
public sealed class TransparencyAction : SyncRevertibleActionHandler
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Value = "EnableTransparency";

    /// <inheritdoc />
    public override string Type => "theme.transparency";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        Set(action.GetBoolean("enabled") == true);

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: false);
        return Reg.GetDword(key, Value, 1) != 0 ? "1" : "0";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context) => Set(snapshot != "0");

    private static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        Reg.SetDword(key, Value, enabled ? 1 : 0);
        User32.BroadcastSettingChange("ImmersiveColorSet");
    }
}

/// <summary><c>wallpaper.set</c>: desktop background.</summary>
public sealed class WallpaperAction : SyncRevertibleActionHandler
{
    private const string DesktopKey = @"Control Panel\Desktop";

    private static readonly Dictionary<string, (string Style, string Tile)> Styles = new()
    {
        ["fill"] = ("10", "0"),
        ["fit"] = ("6", "0"),
        ["stretch"] = ("2", "0"),
        ["tile"] = ("0", "1"),
        ["center"] = ("0", "0"),
        ["span"] = ("22", "0"),
    };

    /// <inheritdoc />
    public override string Type => "wallpaper.set";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var path = Environment.ExpandEnvironmentVariables(action.GetString("path") ?? "");
        if (!File.Exists(path))
            throw new ActionFailedException($"image not found: {path}");
        var (style, tile) = Styles[action.GetString("style") ?? "fill"];
        Apply(style, tile, path);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var current = new StringBuilder(1024);
        User32.SystemParametersInfo(User32.SPI_GETDESKWALLPAPER, (uint)current.Capacity, current, 0);
        using var key = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: false);
        return $"{key.GetValue("WallpaperStyle") as string ?? "10"}|{key.GetValue("TileWallpaper") as string ?? "0"}|{current}";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? "10|0|").Split('|', 3);
        Apply(parts[0], parts[1], parts.Length > 2 ? parts[2] : "");
    }

    private static void Apply(string style, string tile, string path)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true))
        {
            key.SetValue("WallpaperStyle", style, RegistryValueKind.String);
            key.SetValue("TileWallpaper", tile, RegistryValueKind.String);
        }
        if (!User32.SystemParametersInfo(User32.SPI_SETDESKWALLPAPER, 0, path, User32.SPIF_UPDATEINIFILE | User32.SPIF_SENDCHANGE))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused the wallpaper");
    }
}

/// <summary><c>taskbar.autohide</c>: taskbar auto-hide.</summary>
public sealed class TaskbarAutoHideAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "taskbar.autohide";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) => Set(action.GetBoolean("enabled") == true);

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var data = Data();
        var state = (int)User32.SHAppBarMessage(User32.ABM_GETSTATE, ref data).ToUInt32();
        return (state & User32.ABS_AUTOHIDE) != 0 ? "1" : "0";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context) => Set(snapshot == "1");

    private static void Set(bool autoHide)
    {
        var data = Data();
        data.lParam = autoHide ? User32.ABS_AUTOHIDE : User32.ABS_ALWAYSONTOP;
        User32.SHAppBarMessage(User32.ABM_SETSTATE, ref data);
    }

    private static User32.APPBARDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<User32.APPBARDATA>(),
        hWnd = User32.FindWindow("Shell_TrayWnd", null),
    };
}
