using AutoSettings.Core.Events;

namespace AutoSettings.Core.Engine;

/// <summary>Describes the event that automatically reverts an applied profile.</summary>
/// <param name="Kind">The event kind that reverts.</param>
/// <param name="AppName">When set, only events for this exe name revert.</param>
/// <param name="SessionId">When set, only events from this session revert.</param>
/// <param name="RequireLastInstance">For app_closed: only when the last instance closed.</param>
public sealed record RevertRule(SystemEventKind Kind, string? AppName, int? SessionId, bool RequireLastInstance)
{
    /// <summary>Whether <paramref name="e"/> reverts the profile.</summary>
    public bool Matches(SystemEvent e, EventFacts facts)
    {
        if (e.Kind != Kind)
            return false;
        if (SessionId is not null && e.SessionId is not null && SessionId != e.SessionId)
            return false;
        if (AppName is not null && !string.Equals(AppName, e.Process?.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (RequireLastInstance && !facts.IsLastInstance)
            return false;
        return true;
    }

    /// <summary>Plain-language description, e.g. "when chrome.exe loses focus".</summary>
    public string Describe() => Kind switch
    {
        SystemEventKind.AppUnfocused => $"when {AppName ?? "the app"} loses focus",
        SystemEventKind.AppClosed => $"when {AppName ?? "the app"} closes",
        SystemEventKind.Logoff => "when you sign out",
        SystemEventKind.Lock => "when the screen locks",
        _ => $"on {EventNames.TriggerType(Kind)}",
    };

    /// <summary>
    /// Creates the rule for a <c>revert_on</c> value and the event that applied the profile.
    /// Returns <c>null</c> when the profile should stay until reverted manually.
    /// </summary>
    public static RevertRule? Create(string revertOn, SystemEvent? source)
    {
        var app = source?.Process?.Name;
        var session = source?.SessionId;
        return revertOn switch
        {
            "never" => null,
            "app_unfocused" => new RevertRule(SystemEventKind.AppUnfocused, app, session, false),
            "app_closed" => new RevertRule(SystemEventKind.AppClosed, app, session, true),
            "logoff" => new RevertRule(SystemEventKind.Logoff, null, session, false),
            "lock" => new RevertRule(SystemEventKind.Lock, null, session, false),
            _ => source?.Kind switch
            {
                SystemEventKind.AppFocused => new RevertRule(SystemEventKind.AppUnfocused, app, session, false),
                SystemEventKind.AppStarted => new RevertRule(SystemEventKind.AppClosed, app, session, true),
                SystemEventKind.Logon => new RevertRule(SystemEventKind.Logoff, null, session, false),
                SystemEventKind.Unlock => new RevertRule(SystemEventKind.Lock, null, session, false),
                _ => null,
            },
        };
    }
}
