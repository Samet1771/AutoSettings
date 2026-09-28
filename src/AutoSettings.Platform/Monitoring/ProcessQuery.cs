using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AutoSettings.Core.Events;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Monitoring;

/// <summary>Helpers to look up process details.</summary>
public static class ProcessQuery
{
    /// <summary>Full executable path of a process, or null when it cannot be read.</summary>
    public static string? TryGetPath(int processId)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>Session id of a process, or null.</summary>
    /// <remarks>
    /// ProcessIdToSessionId needs query access to the process, which a normal user does not have for the service
    /// (SYSTEM). The system process list, which <see cref="Process.SessionId"/> reads, has every process's session
    /// without that access, like Task Manager.
    /// </remarks>
    public static int? TryGetSessionId(int processId)
    {
        if (Native.ProcessIdToSessionId(processId, out var session))
            return session;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.SessionId;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Describes a running process, or returns null if it already exited.</summary>
    public static ProcessInfo? TryDescribe(int processId)
    {
        var path = TryGetPath(processId);
        if (path is not null)
            return new ProcessInfo(processId, Path.GetFileName(path), path);
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessInfo(processId, process.ProcessName + ".exe");
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Lists running processes, optionally only those in <paramref name="sessionId"/>.</summary>
    public static List<(int SessionId, ProcessInfo Process)> Snapshot(int? sessionId = null)
    {
        var result = new List<(int, ProcessInfo)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id is 0 or 4)
                        continue;
                    var session = process.SessionId;
                    if (sessionId is not null && session != sessionId)
                        continue;
                    var path = TryGetPath(process.Id);
                    var name = path is not null ? Path.GetFileName(path) : process.ProcessName + ".exe";
                    result.Add((session, new ProcessInfo(process.Id, name, path)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Exited while enumerating.
                }
            }
        }
        return result;
    }
}

/// <summary>Converts kernel device paths (\Device\HarddiskVolume3\...) to drive paths (C:\...).</summary>
public static class DevicePaths
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _map;

    /// <summary>Returns the DOS path for <paramref name="ntPath"/>, or null when no drive matches.</summary>
    public static string? ToDosPath(string? ntPath)
    {
        if (string.IsNullOrEmpty(ntPath))
            return null;
        if (ntPath.Length > 2 && ntPath[1] == ':')
            return ntPath;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Dictionary<string, string> map;
            lock (Gate)
                map = _map ??= Build();
            foreach (var (device, drive) in map)
            {
                if (ntPath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                    return drive + ntPath[device.Length..];
            }
            lock (Gate)
                _map = null; // Drives may have been added; rebuild once.
        }
        return null;
    }

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var target = new StringBuilder(1024);
            if (Native.QueryDosDevice($"{letter}:", target, target.Capacity) != 0)
                map[target.ToString()] = $"{letter}:";
        }
        return map;
    }
}
