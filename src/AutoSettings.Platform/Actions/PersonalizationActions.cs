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
/// <remarks>
/// The Windows 11 taskbar does not always apply <c>ABM_SETSTATE</c>. The action therefore checks the result, also
/// writes the value Explorer reads at start (<c>StuckRects3</c>), and optionally restarts Explorer. It fails with an
/// explanation instead of reporting success when the taskbar did not change.
/// </remarks>
public sealed class TaskbarAutoHideAction : SyncRevertibleActionHandler
{
    private const string StuckRectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
    private const int AutoHideByte = 8;
    private const int AutoHideBit = 0x01;

    /// <inheritdoc />
    public override string Type => "taskbar.autohide";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        Set(action.GetBoolean("enabled") == true, action.GetBoolean("restart_explorer") == true);

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) => IsAutoHide() ? "1" : "0";

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context) =>
        Set(snapshot == "1", action.GetBoolean("restart_explorer") == true);

    private static void Set(bool autoHide, bool restartExplorer)
    {
        // 1. The documented way; works on Windows 10 and often on Windows 11.
        SendState(autoHide);
        if (WaitFor(autoHide, TimeSpan.FromMilliseconds(400)))
            return;

        // 2. Save it where Explorer keeps it, tell the shell, and ask again.
        var saved = WriteStuckRects(autoHide);
        User32.BroadcastSettingChange("TraySettings");
        SendState(autoHide);
        if (WaitFor(autoHide, TimeSpan.FromMilliseconds(800)))
            return;

        // 3. Explorer applies the saved value when it starts.
        if (restartExplorer && saved)
        {
            RestartExplorer();
            if (WaitFor(autoHide, TimeSpan.FromSeconds(8)))
                return;
            throw new ActionFailedException("Explorer was restarted, but the taskbar still did not change");
        }

        throw new ActionFailedException(saved
            ? "Windows saved the setting but did not apply it to the taskbar yet; it takes effect when Explorer restarts or you sign in again. Add 'restart_explorer: true' to restart Explorer automatically"
            : "Windows did not change the taskbar setting");
    }

    private static void SendState(bool autoHide)
    {
        var data = Data();
        if (data.hWnd == IntPtr.Zero)
            return;
        data.lParam = autoHide ? User32.ABS_AUTOHIDE : User32.ABS_ALWAYSONTOP;
        User32.SHAppBarMessage(User32.ABM_SETSTATE, ref data);
    }

    private static bool IsAutoHide()
    {
        var data = Data();
        var state = (int)User32.SHAppBarMessage(User32.ABM_GETSTATE, ref data).ToUInt32();
        return (state & User32.ABS_AUTOHIDE) != 0;
    }

    /// <summary>Polls the live state, because the taskbar applies changes asynchronously.</summary>
    private static bool WaitFor(bool autoHide, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            Thread.Sleep(100);
            if (User32.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero && IsAutoHide() == autoHide)
                return true;
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }

    /// <summary>Sets the auto-hide bit in StuckRects3\Settings. Returns false when the value is missing or unexpected.</summary>
    private static bool WriteStuckRects(bool autoHide)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StuckRectsKey, writable: true);
            if (key?.GetValue("Settings") is not byte[] settings || settings.Length <= AutoHideByte)
                return false;
            settings[AutoHideByte] = (byte)(autoHide
                ? settings[AutoHideByte] | AutoHideBit
                : settings[AutoHideByte] & ~AutoHideBit);
            key.SetValue("Settings", settings, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Restarts Explorer in this session so it reads the saved taskbar settings.</summary>
    private static void RestartExplorer()
    {
        var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == session)
                        process.Kill();
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                }
            }
        }

        // Windows usually restarts the shell by itself; start it if it did not.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && User32.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
            Thread.Sleep(200);
        if (User32.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
    }

    private static User32.APPBARDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<User32.APPBARDATA>(),
        hWnd = User32.FindWindow("Shell_TrayWnd", null),
    };
}
