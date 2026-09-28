using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Monitoring;

/// <summary>Starts a process as the user signed in to a session. Only works from a service running as SYSTEM.</summary>
public static class UserProcessLauncher
{
    /// <summary>Starts <paramref name="executable"/> in <paramref name="sessionId"/> as that session's user and returns its process id.</summary>
    public static int Launch(int sessionId, string executable, string arguments)
    {
        if (!Native.WTSQueryUserToken(sessionId, out var userToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot get the user token for session {sessionId}");

        var primaryToken = IntPtr.Zero;
        var environment = IntPtr.Zero;
        try
        {
            if (!Native.DuplicateTokenEx(userToken, Native.TOKEN_ALL_ACCESS, IntPtr.Zero, Native.SecurityImpersonation, Native.TokenPrimary, out primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed");

            if (!Native.CreateEnvironmentBlock(out environment, primaryToken, false))
                environment = IntPtr.Zero;

            var startupInfo = new Native.STARTUPINFO
            {
                cb = Marshal.SizeOf<Native.STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };
            var commandLine = new StringBuilder($"\"{executable}\" {arguments}");
            var flags = environment != IntPtr.Zero ? Native.CREATE_UNICODE_ENVIRONMENT : 0;

            if (!Native.CreateProcessAsUser(primaryToken, executable, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                    environment, Path.GetDirectoryName(executable), ref startupInfo, out var processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot start {executable} in session {sessionId}");

            Native.CloseHandle(processInfo.hThread);
            Native.CloseHandle(processInfo.hProcess);
            return processInfo.dwProcessId;
        }
        finally
        {
            if (environment != IntPtr.Zero)
                Native.DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero)
                Native.CloseHandle(primaryToken);
            Native.CloseHandle(userToken);
        }
    }
}
