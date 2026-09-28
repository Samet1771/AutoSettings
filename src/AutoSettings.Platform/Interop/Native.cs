using System.Runtime.InteropServices;
using System.Text;

namespace AutoSettings.Platform.Interop;

/// <summary>kernel32 / advapi32 / userenv / wtsapi32 declarations.</summary>
internal static class Native
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_ALL_ACCESS = 0xF01FF;
    public const int SecurityImpersonation = 2;
    public const int TokenPrimary = 1;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_NEW_CONSOLE = 0x00000010;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryDosDeviceW")]
    public static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int max);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ProcessIdToSessionId(int processId, out int sessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out int clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out int serverProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // ---------------------------------------------------------------- WTS

    public enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
        WTSConnectState = 8,
        WTSClientProtocolType = 16,
    }

    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WTS_SESSION_INFO
    {
        public int SessionId;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
    public static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, WTS_INFO_CLASS infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSEnumerateSessionsW")]
    public static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

    // ---------------------------------------------------------------- Process creation

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessAsUserW")]
    public static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
