using System.Runtime.InteropServices;
using System.Text;

namespace AutoSettings.Platform.Interop;

/// <summary>user32 / shell32 / dxva2 declarations.</summary>
internal static class User32
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPI_GETDESKWALLPAPER = 0x0073;
    public const uint SPI_GETMOUSESPEED = 0x0070;
    public const uint SPI_SETMOUSESPEED = 0x0071;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    public const int SM_CMONITORS = 80;

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const uint DM_PELSWIDTH = 0x00080000;
    public const uint DM_PELSHEIGHT = 0x00100000;
    public const uint DM_DISPLAYFREQUENCY = 0x00400000;
    public const uint CDS_UPDATEREGISTRY = 0x00000001;
    public const uint CDS_TEST = 0x00000002;
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DISP_CHANGE_RESTART = 1;

    public delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    public static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, string? lParam, uint flags, uint timeout, out IntPtr result);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    public static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr param);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    // ---------------------------------------------------------------- shell32

    public const uint ABM_GETSTATE = 0x00000004;
    public const uint ABM_SETSTATE = 0x0000000A;
    public const int ABS_AUTOHIDE = 0x0000001;
    public const int ABS_ALWAYSONTOP = 0x0000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    public static extern UIntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    /// <summary>QUERY_USER_NOTIFICATION_STATE.</summary>
    public enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningDirect3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out UserNotificationState state);

    // ---------------------------------------------------------------- dxva2 (DDC/CI)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    /// <summary>Tells running apps that a system setting changed.</summary>
    public static void BroadcastSettingChange(string? area)
    {
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, area, SMTO_ABORTIFHUNG, 2000, out _);
    }
}

/// <summary>powrprof declarations.</summary>
internal static class PowrProf
{
    public const uint ACCESS_SCHEME = 16;
    public const uint ERROR_NO_MORE_ITEMS = 259;

    public static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    public static readonly Guid SleepSubgroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    public static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subgroupGuid, uint accessFlags, uint index, ref Guid buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subgroupGuid, IntPtr settingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint value);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr memory);
}
