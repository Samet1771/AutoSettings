using System.Runtime.InteropServices;
using AutoSettings.Core.Engine;

namespace AutoSettings.Platform.Interop;

/// <summary>The Connecting and Configuring Displays (CCD) API.</summary>
internal static class DisplayConfig
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1;
    public const int GET_ADVANCED_COLOR_INFO = 9;
    public const int SET_ADVANCED_COLOR_STATE = 10;
    public const int GET_DPI_SCALE = -3;
    public const int SET_DPI_SCALE = -4;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public RATIONAL refreshRate;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_INFO
    {
        public PATH_SOURCE_INFO sourceInfo;
        public PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MODE_INFO
    {
        public int infoType;
        public uint id;
        public LUID adapterId;
        public ulong data0;
        public ulong data1;
        public ulong data2;
        public ulong data3;
        public ulong data4;
        public ulong data5;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GET_ADVANCED_COLOR_INFO_PACKET
    {
        public DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SET_ADVANCED_COLOR_STATE_PACKET
    {
        public DEVICE_INFO_HEADER header;
        public uint value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DPI_SCALE_GET_PACKET
    {
        public DEVICE_INFO_HEADER header;
        public int minScaleRel;
        public int curScaleRel;
        public int maxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DPI_SCALE_SET_PACKET
    {
        public DEVICE_INFO_HEADER header;
        public int scaleRel;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] PATH_INFO[] paths, ref uint numModeInfoArrayElements, [Out] MODE_INFO[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref GET_ADVANCED_COLOR_INFO_PACKET request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DPI_SCALE_GET_PACKET request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref SET_ADVANCED_COLOR_STATE_PACKET request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DPI_SCALE_SET_PACKET request);

    /// <summary>An active display path: which source (desktop) is shown on which target (monitor).</summary>
    public sealed record ActivePath(LUID SourceAdapter, uint SourceId, LUID TargetAdapter, uint TargetId, string GdiName);

    public static DEVICE_INFO_HEADER Header<T>(int type, LUID adapter, uint id) where T : struct => new()
    {
        type = type,
        size = (uint)Marshal.SizeOf<T>(),
        adapterId = adapter,
        id = id,
    };

    /// <summary>Lists the active display paths with their GDI names (\\.\DISPLAY1, ...).</summary>
    public static List<ActivePath> ActivePaths()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
                break;
            var paths = new PATH_INFO[pathCount];
            var modes = new MODE_INFO[modeCount];
            var result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == 122) // ERROR_INSUFFICIENT_BUFFER: the topology changed, try again
                continue;
            if (result != 0)
                break;

            var list = new List<ActivePath>();
            foreach (var path in paths.Take((int)pathCount))
            {
                var name = new SOURCE_DEVICE_NAME
                {
                    header = Header<SOURCE_DEVICE_NAME>(GET_SOURCE_NAME, path.sourceInfo.adapterId, path.sourceInfo.id),
                    viewGdiDeviceName = "",
                };
                DisplayConfigGetDeviceInfo(ref name);
                list.Add(new ActivePath(path.sourceInfo.adapterId, path.sourceInfo.id, path.targetInfo.adapterId, path.targetInfo.id, name.viewGdiDeviceName ?? ""));
            }
            return list;
        }
        throw new ActionFailedException("Windows did not return the display configuration");
    }

    /// <summary>Selects paths by "all", "primary", a number or a GDI device name.</summary>
    public static List<ActivePath> Select(string? monitor)
    {
        var paths = ActivePaths();
        monitor = string.IsNullOrWhiteSpace(monitor) ? "all" : monitor.Trim();
        if (monitor.Equals("all", StringComparison.OrdinalIgnoreCase))
            return paths;
        var name = Monitors.ResolveDeviceName(monitor);
        var selected = paths.Where(p => string.Equals(p.GdiName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return selected.Count > 0
            ? selected
            : throw new ActionFailedException($"monitor '{monitor}' was not found. Connected: {string.Join(", ", paths.Select(p => p.GdiName))}");
    }
}

/// <summary>Monitor names and the primary monitor.</summary>
internal static class Monitors
{
    private const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>GDI device names of all monitors, primary first.</summary>
    public static List<(string DeviceName, bool Primary)> List()
    {
        var result = new List<(string, bool)>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref User32.RECT rect, IntPtr data) =>
        {
            var info = new User32.MONITORINFOEX { cbSize = Marshal.SizeOf<User32.MONITORINFOEX>(), szDevice = "" };
            if (User32.GetMonitorInfo(hMonitor, ref info))
                result.Add((info.szDevice, (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return result.OrderByDescending(m => m.Item2).ToList();
    }

    /// <summary>The GDI name of the primary monitor.</summary>
    public static string Primary() => List().FirstOrDefault(m => m.Primary).DeviceName ?? @"\\.\DISPLAY1";

    /// <summary>"primary" → primary name; "2" → \\.\DISPLAY2; names pass through.</summary>
    public static string ResolveDeviceName(string monitor)
    {
        if (monitor.Equals("primary", StringComparison.OrdinalIgnoreCase))
            return Primary();
        if (int.TryParse(monitor, out var number))
            return $@"\\.\DISPLAY{number}";
        return monitor;
    }
}
