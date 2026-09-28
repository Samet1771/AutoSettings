namespace AutoSettings.Core.Events;

/// <summary>The kinds of events that can start an automation.</summary>
public enum SystemEventKind
{
    /// <summary>The computer started (cold boot, or a Fast Startup boot).</summary>
    Boot,
    /// <summary>A user signed in.</summary>
    Logon,
    /// <summary>A user signed out.</summary>
    Logoff,
    /// <summary>A user locked the session.</summary>
    Lock,
    /// <summary>A user unlocked the session.</summary>
    Unlock,
    /// <summary>A process started.</summary>
    AppStarted,
    /// <summary>A process exited.</summary>
    AppClosed,
    /// <summary>An app's window became the foreground window.</summary>
    AppFocused,
    /// <summary>An app's window stopped being the foreground window.</summary>
    AppUnfocused,
}

/// <summary>Values for <see cref="SystemEvent.BootType"/>.</summary>
public static class BootTypes
{
    /// <summary>A full (cold) boot.</summary>
    public const string Cold = "cold";

    /// <summary>A Fast Startup (hybrid) boot: the kernel resumed from hibernation.</summary>
    public const string FastStartup = "fast_startup";
}

/// <summary>A Windows user account.</summary>
/// <param name="Name">User name, e.g. <c>samet</c>.</param>
/// <param name="Domain">Domain or computer name, e.g. <c>DESKTOP-1234</c>.</param>
/// <param name="Sid">Security identifier, e.g. <c>S-1-5-21-...</c>.</param>
public sealed record UserInfo(string Name, string? Domain = null, string? Sid = null)
{
    /// <summary><c>DOMAIN\name</c>, or just the name when there is no domain.</summary>
    public string QualifiedName => string.IsNullOrEmpty(Domain) ? Name : $"{Domain}\\{Name}";

    /// <inheritdoc />
    public override string ToString() => QualifiedName;
}

/// <summary>A process.</summary>
/// <param name="Id">Process id.</param>
/// <param name="Name">Executable file name including extension, e.g. <c>chrome.exe</c>.</param>
/// <param name="Path">Full executable path when known.</param>
public sealed record ProcessInfo(int Id, string Name, string? Path = null)
{
    /// <inheritdoc />
    public override string ToString() => $"{Name} (pid {Id})";
}

/// <summary>Something that happened on the computer and may trigger automations.</summary>
public sealed record SystemEvent
{
    /// <summary>What happened.</summary>
    public required SystemEventKind Kind { get; init; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>The Windows session (logon session number) it happened in, when known.</summary>
    public int? SessionId { get; init; }

    /// <summary>The user involved (the one who logged on, or who owns the app).</summary>
    public UserInfo? User { get; init; }

    /// <summary>The app involved, for app events.</summary>
    public ProcessInfo? Process { get; init; }

    /// <summary>The foreground window title, for focus events.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>For <see cref="SystemEventKind.Boot"/>: one of <see cref="BootTypes"/>.</summary>
    public string? BootType { get; init; }

    /// <summary>For session events: whether the session is a Remote Desktop session.</summary>
    public bool IsRemote { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        var parts = new List<string> { Kind.ToString() };
        if (Process is not null) parts.Add(Process.Name);
        if (User is not null) parts.Add($"user {User.QualifiedName}");
        if (SessionId is not null) parts.Add($"session {SessionId}");
        if (BootType is not null) parts.Add(BootType);
        if (IsRemote) parts.Add("remote");
        return string.Join(" · ", parts);
    }
}
