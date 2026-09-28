using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Model;
using AutoSettings.Platform.Interop;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace AutoSettings.Platform.Actions;

/// <summary>Captures and restores a set of HKCU registry values (used by settings stored in several values).</summary>
internal static class RegistryValues
{
    private const string Absent = "-";

    public static string Capture(IEnumerable<(string SubKey, string Name)> values)
    {
        var snapshot = new Dictionary<string, string>();
        foreach (var (subKey, name) in values)
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                snapshot[subKey + "|" + name] = Absent;
                continue;
            }
            var payload = value switch
            {
                byte[] bytes => Convert.ToBase64String(bytes),
                int i => unchecked((uint)i).ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
            snapshot[subKey + "|" + name] = ((int)key!.GetValueKind(name)).ToString(CultureInfo.InvariantCulture) + ":" + payload;
        }
        return JsonSerializer.Serialize(snapshot);
    }

    public static void Restore(string? snapshot)
    {
        if (snapshot is null)
            return;
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot) ?? new Dictionary<string, string>();
        foreach (var (path, stored) in values)
        {
            var separator = path.LastIndexOf('|');
            var subKey = path[..separator];
            var name = path[(separator + 1)..];
            if (stored == Absent)
            {
                using var existing = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
                existing?.DeleteValue(name, throwOnMissingValue: false);
                continue;
            }
            var colon = stored.IndexOf(':');
            var kind = (RegistryValueKind)int.Parse(stored[..colon], CultureInfo.InvariantCulture);
            var payload = stored[(colon + 1)..];
            object value = kind switch
            {
                RegistryValueKind.Binary => Convert.FromBase64String(payload),
                RegistryValueKind.DWord => unchecked((int)uint.Parse(payload, CultureInfo.InvariantCulture)),
                RegistryValueKind.QWord => long.Parse(payload, CultureInfo.InvariantCulture),
                _ => payload,
            };
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            key.SetValue(name, value, kind);
        }
    }
}

/// <summary><c>theme.accent_color</c>.</summary>
public sealed class AccentColorAction : SyncRevertibleActionHandler
{
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Desktop = @"Control Panel\Desktop";

    private static readonly (string, string)[] Values =
    [
        (Dwm, "AccentColor"), (Dwm, "ColorizationColor"), (Dwm, "ColorizationAfterglow"), (Dwm, "ColorPrevalence"),
        (Accent, "AccentColorMenu"), (Accent, "StartColorMenu"), (Accent, "AccentPalette"),
        (Personalize, "ColorPrevalence"), (Desktop, "AutoColorization"),
    ];

    /// <inheritdoc />
    public override string Type => "theme.accent_color";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var hex = action.GetString("color")!.TrimStart('#');
        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        var abgr = unchecked((int)(0xFF000000u | (uint)b << 16 | (uint)g << 8 | r));
        var argb = unchecked((int)(0xC4000000u | (uint)r << 16 | (uint)g << 8 | b));

        using (var dwm = Registry.CurrentUser.CreateSubKey(Dwm, writable: true))
        {
            Reg.SetDword(dwm, "AccentColor", abgr);
            Reg.SetDword(dwm, "ColorizationColor", argb);
            Reg.SetDword(dwm, "ColorizationAfterglow", argb);
            if (action.GetBoolean("show_on_title_bars") is { } titleBars)
                Reg.SetDword(dwm, "ColorPrevalence", titleBars ? 1 : 0);
        }
        using (var accent = Registry.CurrentUser.CreateSubKey(Accent, writable: true))
        {
            Reg.SetDword(accent, "AccentColorMenu", abgr);
            Reg.SetDword(accent, "StartColorMenu", Shade(r, g, b, 0.7));
            accent.SetValue("AccentPalette", Palette(r, g, b), RegistryValueKind.Binary);
        }
        if (action.GetBoolean("show_on_taskbar") is { } taskbar)
        {
            using var personalize = Registry.CurrentUser.CreateSubKey(Personalize, writable: true);
            Reg.SetDword(personalize, "ColorPrevalence", taskbar ? 1 : 0);
        }
        using (var desktop = Registry.CurrentUser.CreateSubKey(Desktop, writable: true))
            desktop.SetValue("AutoColorization", "0", RegistryValueKind.String);
        User32.BroadcastSettingChange("ImmersiveColorSet");
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) => RegistryValues.Capture(Values);

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        RegistryValues.Restore(snapshot);
        User32.BroadcastSettingChange("ImmersiveColorSet");
    }

    private static int Shade(byte r, byte g, byte b, double factor) =>
        unchecked((int)(0xFF000000u | (uint)Scale(b, factor) << 16 | (uint)Scale(g, factor) << 8 | Scale(r, factor)));

    private static byte Scale(byte channel, double factor) =>
        (byte)Math.Clamp(factor >= 1 ? channel + (255 - channel) * (factor - 1) : channel * factor, 0, 255);

    /// <summary>Eight RGBA colors from light to dark, like the palette Windows generates.</summary>
    private static byte[] Palette(byte r, byte g, byte b)
    {
        double[] factors = [1.6, 1.4, 1.2, 1.0, 0.8, 0.6, 0.4, 0.3];
        var palette = new byte[32];
        for (var i = 0; i < factors.Length; i++)
        {
            palette[i * 4] = Scale(r, factors[i]);
            palette[i * 4 + 1] = Scale(g, factors[i]);
            palette[i * 4 + 2] = Scale(b, factors[i]);
            palette[i * 4 + 3] = 0;
        }
        return palette;
    }
}

/// <summary><c>display.hdr</c>: advanced color on HDR-capable displays.</summary>
public sealed class HdrAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "display.hdr";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var state = action.GetString("state") ?? "toggle";
        var changed = 0;
        foreach (var path in DisplayConfig.Select(action.GetString("monitor")))
        {
            if (Read(path) is not { } enabled)
                continue;
            Set(path, state switch { "on" => true, "off" => false, _ => !enabled });
            changed++;
        }
        if (changed == 0)
            throw new ActionFailedException("none of the selected displays supports HDR");
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) =>
        JsonSerializer.Serialize(DisplayConfig.Select(action.GetString("monitor"))
            .Where(p => Read(p) is not null)
            .ToDictionary(p => p.GdiName, p => Read(p) == true));

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var states = JsonSerializer.Deserialize<Dictionary<string, bool>>(snapshot ?? "{}") ?? new Dictionary<string, bool>();
        foreach (var path in DisplayConfig.ActivePaths())
        {
            if (states.TryGetValue(path.GdiName, out var enabled))
                Set(path, enabled);
        }
    }

    /// <summary>Whether HDR is on, or null when the display does not support it.</summary>
    private static bool? Read(DisplayConfig.ActivePath path)
    {
        var info = new DisplayConfig.GET_ADVANCED_COLOR_INFO_PACKET
        {
            header = DisplayConfig.Header<DisplayConfig.GET_ADVANCED_COLOR_INFO_PACKET>(DisplayConfig.GET_ADVANCED_COLOR_INFO, path.TargetAdapter, path.TargetId),
        };
        if (DisplayConfig.DisplayConfigGetDeviceInfo(ref info) != 0 || (info.value & 1) == 0)
            return null;
        return (info.value & 2) != 0;
    }

    private static void Set(DisplayConfig.ActivePath path, bool enabled)
    {
        var packet = new DisplayConfig.SET_ADVANCED_COLOR_STATE_PACKET
        {
            header = DisplayConfig.Header<DisplayConfig.SET_ADVANCED_COLOR_STATE_PACKET>(DisplayConfig.SET_ADVANCED_COLOR_STATE, path.TargetAdapter, path.TargetId),
            value = enabled ? 1u : 0u,
        };
        var result = DisplayConfig.DisplayConfigSetDeviceInfo(ref packet);
        if (result != 0)
            throw new ActionFailedException($"Windows refused to turn HDR {(enabled ? "on" : "off")} on {path.GdiName} (error {result})");
    }
}

/// <summary><c>display.scaling</c>: per-display scale.</summary>
public sealed class ScalingAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "display.scaling";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var percent = (int)(action.GetInteger("percent") ?? 100);
        foreach (var path in DisplayConfig.Select(action.GetString("monitor") ?? "primary"))
            Set(path, percent);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) =>
        JsonSerializer.Serialize(DisplayConfig.Select(action.GetString("monitor") ?? "primary").ToDictionary(p => p.GdiName, p => Read(p).Current));

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, int>>(snapshot ?? "{}") ?? new Dictionary<string, int>();
        foreach (var path in DisplayConfig.ActivePaths())
        {
            if (values.TryGetValue(path.GdiName, out var percent))
                Set(path, percent);
        }
    }

    private static (int Recommended, int Current, int Minimum, int Maximum) Read(DisplayConfig.ActivePath path)
    {
        var packet = new DisplayConfig.DPI_SCALE_GET_PACKET
        {
            header = DisplayConfig.Header<DisplayConfig.DPI_SCALE_GET_PACKET>(DisplayConfig.GET_DPI_SCALE, path.SourceAdapter, path.SourceId),
        };
        if (DisplayConfig.DisplayConfigGetDeviceInfo(ref packet) != 0)
            throw new ActionFailedException($"cannot read the scale of {path.GdiName}");
        var steps = BuiltInActions.ScaleSteps;
        var recommendedIndex = Math.Abs(packet.minScaleRel);
        int At(int index) => steps[Math.Clamp(index, 0, steps.Count - 1)];
        return (At(recommendedIndex), At(recommendedIndex + packet.curScaleRel), At(0), At(recommendedIndex + packet.maxScaleRel));
    }

    private static void Set(DisplayConfig.ActivePath path, int percent)
    {
        var (recommended, _, _, maximum) = Read(path);
        var steps = BuiltInActions.ScaleSteps;
        var target = steps.ToList().IndexOf(percent);
        if (target < 0 || percent > maximum)
            throw new ActionFailedException($"{percent}% is not available on {path.GdiName} (maximum {maximum}%)");
        var packet = new DisplayConfig.DPI_SCALE_SET_PACKET
        {
            header = DisplayConfig.Header<DisplayConfig.DPI_SCALE_SET_PACKET>(DisplayConfig.SET_DPI_SCALE, path.SourceAdapter, path.SourceId),
            scaleRel = target - steps.ToList().IndexOf(recommended),
        };
        var result = DisplayConfig.DisplayConfigSetDeviceInfo(ref packet);
        if (result != 0)
            throw new ActionFailedException($"Windows refused scale {percent}% on {path.GdiName} (error {result})");
    }
}

/// <summary><c>display.primary</c>: the main display.</summary>
public sealed class PrimaryMonitorAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "display.primary";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        MakePrimary(Monitors.ResolveDeviceName(action.GetString("monitor") ?? "1"));

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) => Monitors.Primary();

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        if (!string.IsNullOrEmpty(snapshot))
            MakePrimary(snapshot);
    }

    private static void MakePrimary(string deviceName)
    {
        var monitors = Monitors.List();
        if (!monitors.Any(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)))
            throw new ActionFailedException($"monitor {deviceName} was not found. Connected: {string.Join(", ", monitors.Select(m => m.DeviceName))}");
        if (monitors.Any(m => m.Primary && string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)))
            return;

        var target = Current(deviceName);
        var offsetX = target.dmPositionX;
        var offsetY = target.dmPositionY;
        foreach (var (name, _) in monitors)
        {
            var mode = Current(name);
            mode.dmPositionX -= offsetX;
            mode.dmPositionY -= offsetY;
            mode.dmFields = Misc.DM_POSITION;
            var flags = User32.CDS_UPDATEREGISTRY | Misc.CDS_NORESET;
            if (string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase))
                flags |= Misc.CDS_SET_PRIMARY;
            var result = User32.ChangeDisplaySettingsEx(name, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
            if (result != User32.DISP_CHANGE_SUCCESSFUL)
                throw new ActionFailedException($"Windows refused to move {name} (error {result})");
        }
        Misc.ChangeDisplaySettingsExApply(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
    }

    private static User32.DEVMODE Current(string deviceName)
    {
        var mode = new User32.DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (ushort)Marshal.SizeOf<User32.DEVMODE>() };
        if (!User32.EnumDisplaySettings(deviceName, User32.ENUM_CURRENT_SETTINGS, ref mode))
            throw new ActionFailedException($"cannot read the settings of {deviceName}");
        return mode;
    }
}

/// <summary><c>display.night_light</c> (best effort: Windows stores the state in an undocumented blob).</summary>
public sealed class NightLightAction : SyncRevertibleActionHandler
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private const int OffLength = 41;
    private const int OnLength = 43;

    /// <inheritdoc />
    public override string Type => "display.night_light";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var on = IsOn(Read());
        var target = action.GetString("state") switch { "on" => true, "off" => false, _ => !on };
        Set(target);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) => IsOn(Read()) ? "on" : "off";

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context) => Set(snapshot == "on");

    private static byte[] Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue("Data") as byte[]
            ?? throw new ActionFailedException("Night light has never been used on this account; turn it on once in Settings first (settings.open: night_light)");
    }

    private static bool IsOn(byte[] data) => data.Length > 18 && data[18] == 0x15;

    private static void Set(bool on)
    {
        var data = Read();
        if (IsOn(data) == on)
            return;
        byte[] updated;
        if (on)
        {
            if (data.Length != OffLength)
                throw Unrecognized();
            updated = new byte[OnLength];
            Array.Copy(data, 0, updated, 0, 23);
            Array.Copy(data, 23, updated, 25, OffLength - 23);
            updated[18] = 0x15;
            updated[23] = 0x10;
            updated[24] = 0x00;
        }
        else
        {
            if (data.Length != OnLength)
                throw Unrecognized();
            updated = new byte[OffLength];
            Array.Copy(data, 0, updated, 0, 23);
            Array.Copy(data, 25, updated, 23, OnLength - 25);
            updated[18] = 0x13;
        }
        // Bump the change timestamp so Windows notices.
        for (var i = 10; i < 15; i++)
        {
            if (updated[i] != 0xFF)
            {
                updated[i]++;
                break;
            }
        }
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue("Data", updated, RegistryValueKind.Binary);
    }

    private static ActionFailedException Unrecognized() =>
        new("the Night light setting format of this Windows version is not recognized; use settings.open with page night_light");
}

/// <summary><c>power.mode</c>: the power mode slider (overlay scheme).</summary>
public sealed class PowerModeAction : SyncRevertibleActionHandler
{
    private static readonly Dictionary<string, Guid> Modes = new()
    {
        ["best_efficiency"] = new("961cc777-2547-4f9d-8174-7d86181b8a7a"),
        ["balanced"] = Guid.Empty,
        ["best_performance"] = new("ded574b5-45a0-4f42-8737-46345c09c238"),
    };

    /// <inheritdoc />
    public override string Type => "power.mode";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) =>
        Set(Modes[action.GetString("mode") ?? "balanced"]);

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) =>
        Misc.PowerGetEffectiveOverlayScheme(out var current) == 0 ? current.ToString() : null;

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        if (Guid.TryParse(snapshot, out var mode))
            Set(mode);
    }

    private static void Set(Guid mode)
    {
        var result = Misc.PowerSetActiveOverlayScheme(mode);
        if (result != 0)
            throw new ActionFailedException($"power modes are not available on this computer (error {result})");
    }
}

/// <summary><c>notifications.banners</c>.</summary>
public sealed class NotificationBannersAction : SyncRevertibleActionHandler
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    /// <inheritdoc />
    public override string Type => "notifications.banners";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context) => Set(action.GetBoolean("enabled") == true);

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: false);
        return Reg.GetDword(key, "ToastEnabled", 1) != 0 ? "1" : "0";
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context) => Set(snapshot != "0");

    private static void Set(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
            Reg.SetDword(key, "ToastEnabled", enabled ? 1 : 0);
        User32.BroadcastSettingChange(null);
    }
}

/// <summary><c>notifications.do_not_disturb</c> (best effort: Windows Notification Facility state).</summary>
public sealed class DoNotDisturbAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "notifications.do_not_disturb";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var profile = action.GetString("state") == "on"
            ? action.GetString("level") == "alarms" ? 2 : 1
            : 0;
        Set(profile);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context) =>
        Read()?.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        if (int.TryParse(snapshot, CultureInfo.InvariantCulture, out var profile))
            Set(profile);
    }

    private static int? Read()
    {
        var state = Misc.WnfQuietHoursProfile;
        var buffer = new byte[4];
        var size = buffer.Length;
        return Misc.NtQueryWnfStateData(ref state, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref size) == 0 && size >= 4
            ? BitConverter.ToInt32(buffer, 0)
            : null;
    }

    private static void Set(int profile)
    {
        var state = Misc.WnfQuietHoursProfile;
        var buffer = BitConverter.GetBytes(profile);
        var status = Misc.NtUpdateWnfStateData(ref state, buffer, buffer.Length, IntPtr.Zero, IntPtr.Zero, 0, 0);
        if (status != 0 || Read() != profile)
            throw new ActionFailedException($"Windows did not accept the Do Not Disturb change (status 0x{status:X8}); use settings.open with page focus");
    }
}

/// <summary><c>keyboard.layout</c>.</summary>
public sealed class KeyboardLayoutAction : SyncRevertibleActionHandler
{
    /// <inheritdoc />
    public override string Type => "keyboard.layout";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var layout = action.GetString("layout") ?? "";
        var klid = layout.Length == 8 && layout.All(Uri.IsHexDigit) ? layout : LayoutIdFor(layout);
        var hkl = Misc.LoadKeyboardLayout(klid, Misc.KLF_ACTIVATE);
        if (hkl == IntPtr.Zero)
            throw new ActionFailedException($"keyboard layout '{layout}' is not installed");
        Activate(hkl);
    }

    /// <inheritdoc />
    protected override string? Capture(ComponentConfig action, ActionContext context)
    {
        var thread = User32.GetWindowThreadProcessId(User32.GetForegroundWindow(), out _);
        return Misc.GetKeyboardLayout(thread).ToInt64().ToString("X", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    protected override void Restore(ComponentConfig action, string? snapshot, ActionContext context)
    {
        if (long.TryParse(snapshot, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && value != 0)
            Activate(new IntPtr(value));
    }

    private static void Activate(IntPtr hkl)
    {
        Misc.PostMessage(User32.GetForegroundWindow(), Misc.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        var copy = hkl;
        Misc.SystemParametersInfo(Misc.SPI_SETDEFAULTINPUTLANG, 0, ref copy, User32.SPIF_SENDCHANGE);
    }

    private static string LayoutIdFor(string languageTag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageTag).KeyboardLayoutId.ToString("X8", CultureInfo.InvariantCulture);
        }
        catch (CultureNotFoundException)
        {
            throw new ActionFailedException($"'{languageTag}' is not a known language tag (examples: tr-TR, en-US) or layout id (0000041F)");
        }
    }
}

/// <summary><c>radio.airplane_mode</c>: all radios off, or all on.</summary>
public sealed class AirplaneModeAction : IRevertibleActionHandler
{
    /// <inheritdoc />
    public string Type => "radio.airplane_mode";

    /// <inheritdoc />
    public async Task ExecuteAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken)
    {
        var target = action.GetString("state") == "on" ? RadioState.Off : RadioState.On;
        foreach (var radio in await RadiosAsync())
            await radio.SetStateAsync(target);
    }

    /// <inheritdoc />
    public async Task<string?> CaptureAsync(ComponentConfig action, ActionContext context, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize((await RadiosAsync()).Select(r => r.State == RadioState.On).ToList());

    /// <inheritdoc />
    public async Task RestoreAsync(ComponentConfig action, string? snapshot, ActionContext context, CancellationToken cancellationToken)
    {
        var states = JsonSerializer.Deserialize<List<bool>>(snapshot ?? "[]") ?? [];
        var radios = await RadiosAsync();
        for (var i = 0; i < radios.Count && i < states.Count; i++)
            await radios[i].SetStateAsync(states[i] ? RadioState.On : RadioState.Off);
    }

    private static async Task<List<Radio>> RadiosAsync()
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            throw new ActionFailedException("Windows denied access to the radios. Check Settings > Privacy & security > Radios.");
        return (await Radio.GetRadiosAsync()).OrderBy(r => r.Kind).ThenBy(r => r.Name).ToList();
    }
}

/// <summary><c>settings.open</c>.</summary>
public sealed class SettingsOpenAction : SyncActionHandler
{
    /// <inheritdoc />
    public override string Type => "settings.open";

    /// <inheritdoc />
    protected override void Execute(ComponentConfig action, ActionContext context)
    {
        var page = action.GetString("page") ?? "display";
        if (!BuiltInActions.SettingsPages.TryGetValue(page, out var uri))
            throw new ActionFailedException($"unknown Settings page '{page}'");
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            throw new ActionFailedException($"cannot open {uri}: {ex.Message}", ex);
        }
    }
}
