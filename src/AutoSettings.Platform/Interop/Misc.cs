using System.Runtime.InteropServices;

namespace AutoSettings.Platform.Interop;

/// <summary>Keyboard layouts, power overlay schemes and Windows Notification Facility (WNF) state.</summary>
internal static class Misc
{
    public const uint KLF_ACTIVATE = 0x00000001;
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint SPI_SETDEFAULTINPUTLANG = 0x005A;
    public const uint DM_POSITION = 0x00000020;
    public const uint CDS_SET_PRIMARY = 0x00000010;
    public const uint CDS_NORESET = 0x10000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadKeyboardLayoutW", SetLastError = true)]
    public static extern IntPtr LoadKeyboardLayout(string klid, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfo(uint action, uint param, ref IntPtr value, uint winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    public static extern int ChangeDisplaySettingsExApply(string? deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr param);

    [DllImport("powrprof.dll")]
    public static extern uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    /// <summary>WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: the Focus assist / Do Not Disturb profile.</summary>
    public const ulong WnfQuietHoursProfile = 0x0D83063EA3BF1C75;

    [DllImport("ntdll.dll")]
    public static extern int NtUpdateWnfStateData(ref ulong stateName, byte[] buffer, int length, IntPtr typeId, IntPtr explicitScope, uint matchingChangeStamp, int checkStamp);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope, out uint changeStamp, byte[] buffer, ref int bufferSize);
}
