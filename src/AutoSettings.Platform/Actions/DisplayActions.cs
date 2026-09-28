using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Actions;

/// <summary><c>display.brightness</c>: laptop panel via WMI, external monitors via DDC/CI.</summary>
public sealed class BrightnessAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "display.brightness";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var level = (int)(action.GetInteger("level") ?? 100);
        var monitor = action.GetString("monitor") ?? "all";
        var changed = false;
        if (monitor != "external")
            changed |= SetInternal(level);
        if (monitor != "internal")
            changed |= ForEachExternal((handle, _) => SetExternal(handle, level)) > 0;
        if (!changed)
        {
            throw new ActionFailedException(monitor == "internal"
                ? "this computer has no built-in screen whose brightness Windows can control"
                : "no screen accepted the brightness change (external monitors need DDC/CI enabled in their menu)");
        }
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var monitor = action.GetString("monitor") ?? "all";
        var internalLevel = monitor != "external" ? GetInternal() : null;
        var external = new List<string>();
        if (monitor != "internal")
        {
            ForEachExternal((handle, _) =>
            {
                external.Add(GetExternal(handle)?.ToString(CultureInfo.InvariantCulture) ?? "");
                return true;
            });
        }
        return $"{internalLevel?.ToString(CultureInfo.InvariantCulture) ?? ""};{string.Join(",", external)}";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? ";").Split(';', 2);
        if (int.TryParse(parts[0], CultureInfo.InvariantCulture, out var internalLevel))
            SetInternal(internalLevel);
        var external = parts.Length > 1 ? parts[1].Split(',') : Array.Empty<string>();
        ForEachExternal((handle, index) =>
            index < external.Length && int.TryParse(external[index], CultureInfo.InvariantCulture, out var level) && SetExternal(handle, level));
    }

    private static int? GetInternal()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                    return Convert.ToInt32(item["CurrentBrightness"], CultureInfo.InvariantCulture);
            }
        }
        catch (ManagementException)
        {
        }
        return null;
    }

    private static bool SetInternal(int level)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            var any = false;
            foreach (ManagementObject item in searcher.Get())
            {
                using (item)
                {
                    item.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(level, 0, 100) });
                    any = true;
                }
            }
            return any;
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    private static int? GetExternal(IntPtr handle)
    {
        if (!User32.GetMonitorBrightness(handle, out var min, out var current, out var max) || max <= min)
            return null;
        return (int)Math.Round((current - min) * 100.0 / (max - min));
    }

    private static bool SetExternal(IntPtr handle, int level)
    {
        if (!User32.GetMonitorBrightness(handle, out var min, out _, out var max) || max <= min)
            return false;
        var value = min + (uint)Math.Round((max - min) * Math.Clamp(level, 0, 100) / 100.0);
        return User32.SetMonitorBrightness(handle, value);
    }

    /// <summary>Calls <paramref name="callback"/> for every physical monitor; returns how many returned true.</summary>
    private static int ForEachExternal(Func<IntPtr, int, bool> callback)
    {
        var monitors = new List<IntPtr>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref User32.RECT rect, IntPtr data) =>
        {
            monitors.Add(hMonitor);
            return true;
        }, IntPtr.Zero);

        var index = 0;
        var succeeded = 0;
        foreach (var monitor in monitors)
        {
            if (!User32.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
                continue;
            var physical = new User32.PHYSICAL_MONITOR[count];
            if (!User32.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
                continue;
            try
            {
                foreach (var item in physical)
                {
                    if (callback(item.hPhysicalMonitor, index++))
                        succeeded++;
                }
            }
            finally
            {
                User32.DestroyPhysicalMonitors(count, physical);
            }
        }
        return succeeded;
    }
}

/// <summary><c>display.resolution</c>: resolution and refresh rate.</summary>
public sealed class ResolutionAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "display.resolution";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var device = Device(action);
        var mode = Current(device, action.GetString("monitor"));
        uint fields = 0;
        if (action.GetInteger("width") is { } width && action.GetInteger("height") is { } height)
        {
            mode.dmPelsWidth = (uint)width;
            mode.dmPelsHeight = (uint)height;
            fields |= User32.DM_PELSWIDTH | User32.DM_PELSHEIGHT;
        }
        if (action.GetInteger("refresh_rate") is { } refresh)
        {
            mode.dmDisplayFrequency = (uint)refresh;
            fields |= User32.DM_DISPLAYFREQUENCY;
        }
        mode.dmFields = fields;
        Apply(device, mode);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var mode = Current(Device(action), action.GetString("monitor"));
        return string.Create(CultureInfo.InvariantCulture, $"{mode.dmPelsWidth},{mode.dmPelsHeight},{mode.dmDisplayFrequency}");
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var parts = (snapshot ?? "").Split(',');
        if (parts.Length != 3)
            return;
        var device = Device(action);
        var mode = Current(device, action.GetString("monitor"));
        mode.dmPelsWidth = uint.Parse(parts[0], CultureInfo.InvariantCulture);
        mode.dmPelsHeight = uint.Parse(parts[1], CultureInfo.InvariantCulture);
        mode.dmDisplayFrequency = uint.Parse(parts[2], CultureInfo.InvariantCulture);
        mode.dmFields = User32.DM_PELSWIDTH | User32.DM_PELSHEIGHT | User32.DM_DISPLAYFREQUENCY;
        Apply(device, mode);
    }

    private static string? Device(ComponentConfig action)
    {
        var monitor = action.GetString("monitor");
        return string.IsNullOrWhiteSpace(monitor) || monitor.Equals("primary", StringComparison.OrdinalIgnoreCase) ? null : monitor;
    }

    private static User32.DEVMODE Current(string? device, string? label)
    {
        var mode = new User32.DEVMODE
        {
            dmDeviceName = "",
            dmFormName = "",
            dmSize = (ushort)Marshal.SizeOf<User32.DEVMODE>(),
        };
        if (!User32.EnumDisplaySettings(device, User32.ENUM_CURRENT_SETTINGS, ref mode))
            throw new ActionFailedException($"monitor '{label ?? "primary"}' was not found");
        return mode;
    }

    private static void Apply(string? device, User32.DEVMODE mode)
    {
        var description = $"{mode.dmPelsWidth}x{mode.dmPelsHeight} at {mode.dmDisplayFrequency} Hz";
        var test = User32.ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, User32.CDS_TEST, IntPtr.Zero);
        if (test != User32.DISP_CHANGE_SUCCESSFUL)
            throw new ActionFailedException($"the monitor does not support {description} (Windows error {test})");
        var result = User32.ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, User32.CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (result == User32.DISP_CHANGE_RESTART)
            throw new ActionFailedException($"{description} needs a restart to take effect");
        if (result != User32.DISP_CHANGE_SUCCESSFUL)
            throw new ActionFailedException($"Windows could not switch to {description} (error {result})");
    }
}
