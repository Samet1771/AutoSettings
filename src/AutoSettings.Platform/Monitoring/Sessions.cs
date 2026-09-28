using System.Runtime.InteropServices;
using System.Security.Principal;
using AutoSettings.Core.Events;
using AutoSettings.Platform.Interop;

namespace AutoSettings.Platform.Monitoring;

/// <summary>A Windows logon session.</summary>
/// <param name="SessionId">Session number.</param>
/// <param name="User">Signed-in user, or null for sessions without a user.</param>
/// <param name="IsActive">Whether the session is connected (not disconnected).</param>
/// <param name="IsRemote">Whether it is a Remote Desktop session.</param>
public sealed record SessionInfo(int SessionId, UserInfo? User, bool IsActive, bool IsRemote);

/// <summary>Queries Windows sessions (Remote Desktop Services API). Works from the service.</summary>
public static class Sessions
{
    private const int WTSClientProtocolRdp = 2;

    /// <summary>The user signed in to <paramref name="sessionId"/>, or null.</summary>
    public static UserInfo? GetUser(int sessionId)
    {
        var name = QueryString(sessionId, Native.WTS_INFO_CLASS.WTSUserName);
        if (string.IsNullOrEmpty(name))
            return null;
        var domain = QueryString(sessionId, Native.WTS_INFO_CLASS.WTSDomainName);
        return new UserInfo(name, string.IsNullOrEmpty(domain) ? null : domain, TryGetSid(domain, name));
    }

    /// <summary>Whether <paramref name="sessionId"/> is a Remote Desktop session.</summary>
    public static bool IsRemote(int sessionId)
    {
        if (!Native.WTSQuerySessionInformation(IntPtr.Zero, sessionId, Native.WTS_INFO_CLASS.WTSClientProtocolType, out var buffer, out _))
            return false;
        try
        {
            return Marshal.ReadInt16(buffer) == WTSClientProtocolRdp;
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
    }

    /// <summary>All sessions that have a signed-in user.</summary>
    public static IReadOnlyList<SessionInfo> List()
    {
        var result = new List<SessionInfo>();
        if (!Native.WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count))
            return result;
        try
        {
            var size = Marshal.SizeOf<Native.WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Native.WTS_SESSION_INFO>(buffer + i * size);
                if (info.SessionId == 0)
                    continue;
                var user = GetUser(info.SessionId);
                if (user is null)
                    continue;
                result.Add(new SessionInfo(info.SessionId, user, info.State == Native.WTS_CONNECTSTATE_CLASS.WTSActive, IsRemote(info.SessionId)));
            }
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
        return result;
    }

    /// <summary>The user running the current process.</summary>
    public static UserInfo CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var parts = identity.Name.Split('\\', 2);
        return parts.Length == 2
            ? new UserInfo(parts[1], parts[0], identity.User?.Value)
            : new UserInfo(identity.Name, null, identity.User?.Value);
    }

    /// <summary>The session of the current process.</summary>
    public static int CurrentSessionId() => System.Diagnostics.Process.GetCurrentProcess().SessionId;

    private static string? TryGetSid(string? domain, string name)
    {
        try
        {
            var account = string.IsNullOrEmpty(domain) ? new NTAccount(name) : new NTAccount(domain, name);
            return account.Translate(typeof(SecurityIdentifier)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    private static string? QueryString(int sessionId, Native.WTS_INFO_CLASS infoClass)
    {
        if (!Native.WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Native.WTSFreeMemory(buffer);
        }
    }
}
