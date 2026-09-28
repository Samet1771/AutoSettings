using System.IO.Pipes;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Monitoring;

/// <summary>Identifies the process on the other end of a named pipe.</summary>
public static class PipeIdentity
{
    /// <summary>Process id of the connected client, or null.</summary>
    public static int? ClientProcessId(NamedPipeServerStream pipe) =>
        Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) ? pid : null;

    /// <summary>Process id of the server, or null.</summary>
    public static int? ServerProcessId(NamedPipeClientStream pipe) =>
        Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) ? pid : null;
}
