using AutoSettings.Core.Events;

namespace AutoSettings.Core.Catalog;

/// <summary>The built-in trigger types.</summary>
public static class BuiltInTriggers
{
    /// <summary>Category for session triggers.</summary>
    public const string SessionCategory = "Computer & sign-in";

    /// <summary>Category for app triggers.</summary>
    public const string AppsCategory = "Apps";

    private static FieldDescriptor UserFilter(string verb) => Fields.Users(
        "user",
        $"Only fire when one of these users {verb}. Accepts a user name (samet), DOMAIN\\name, a SID, or wildcards (admin*). Leave empty for any user.",
        example: "samet");

    private static FieldDescriptor AppField(string verb) => Fields.Apps(
        "app",
        $"The app(s) that {verb}. Use the exe name (chrome or chrome.exe), a full path (C:\\Games\\game.exe) or wildcards (*steam*). Use * for any app.",
        required: true,
        example: "chrome.exe");

    /// <summary>All built-in triggers.</summary>
    public static IEnumerable<ComponentDescriptor> All =>
    [
        new()
        {
            Type = "boot",
            Kind = ComponentKind.Trigger,
            Category = SessionCategory,
            Title = "Computer started",
            Description = "Fires once each time the computer starts, before anyone signs in.",
            EventKind = SystemEventKind.Boot,
            AvailableIn = ScopeSupport.Machine,
            Fields =
            [
                Fields.Boolean("include_fast_startup",
                    "Also fire when Windows starts with Fast Startup (the default shutdown mode on most PCs, which is really a hibernate). Turn off to react to full restarts only.",
                    defaultValue: true),
            ],
            Example = """
                type: boot
                include_fast_startup: true
                """,
            Notes = "Only available in machine automations, because no user is signed in at boot. To change a user's own settings (theme, audio, ...) at startup, use the `logon` trigger instead. The service remembers the boot time, so restarting the service does not fire this trigger again.",
        },
        new()
        {
            Type = "logon",
            Kind = ComponentKind.Trigger,
            Category = SessionCategory,
            Title = "User signed in",
            Description = "Fires when a user signs in to Windows. The user who signed in is available to conditions and to actions as {{ user }}.",
            EventKind = SystemEventKind.Logon,
            Fields =
            [
                UserFilter("signs in"),
                Fields.Choice("session", "Which kind of sign-in: at the computer (local), over Remote Desktop (remote), or either (any).",
                    ["any", "local", "remote"], defaultValue: "any"),
            ],
            Example = """
                type: logon
                user: samet
                session: local
                """,
            Notes = "In personal automations this fires when you sign in (the agent starts right after sign-in and receives the event from the service). Signing back in after locking the screen is an `unlock`, not a `logon`.",
        },
        new()
        {
            Type = "logoff",
            Kind = ComponentKind.Trigger,
            Category = SessionCategory,
            Title = "User signed out",
            Description = "Fires when a user signs out.",
            EventKind = SystemEventKind.Logoff,
            Fields = [UserFilter("signs out")],
            Example = """
                type: logoff
                user: samet
                """,
            Notes = "Actions that take a long time may be cut short because Windows is closing the session.",
        },
        new()
        {
            Type = "lock",
            Kind = ComponentKind.Trigger,
            Category = SessionCategory,
            Title = "Screen locked",
            Description = "Fires when a user locks the computer (Win+L, or automatically after inactivity).",
            EventKind = SystemEventKind.Lock,
            Fields = [UserFilter("locks the screen")],
            Example = "type: lock",
        },
        new()
        {
            Type = "unlock",
            Kind = ComponentKind.Trigger,
            Category = SessionCategory,
            Title = "Screen unlocked",
            Description = "Fires when a user unlocks the computer.",
            EventKind = SystemEventKind.Unlock,
            Fields = [UserFilter("unlocks the screen")],
            Example = "type: unlock",
        },
        new()
        {
            Type = "app_started",
            Kind = ComponentKind.Trigger,
            Category = AppsCategory,
            Title = "App started",
            Description = "Fires when an app starts.",
            EventKind = SystemEventKind.AppStarted,
            Fields =
            [
                AppField("start"),
                Fields.Choice("instance",
                    "Many apps (browsers, games, Steam) start several processes. 'first' fires only when the first copy starts; 'any' fires for every process.",
                    ["first", "any"], defaultValue: "first"),
                UserFilter("start the app"),
            ],
            Example = """
                type: app_started
                app: [steam.exe, epicgameslauncher.exe]
                """,
            Notes = "Detected instantly by the service using Windows event tracing. If the service is not running, the agent falls back to checking the process list every 2 seconds.",
        },
        new()
        {
            Type = "app_closed",
            Kind = ComponentKind.Trigger,
            Category = AppsCategory,
            Title = "App closed",
            Description = "Fires when an app exits.",
            EventKind = SystemEventKind.AppClosed,
            Fields =
            [
                AppField("close"),
                Fields.Choice("instance",
                    "'last' fires only when the last running copy exits (the app is fully closed); 'any' fires for every process that exits.",
                    ["last", "any"], defaultValue: "last"),
                UserFilter("close the app"),
            ],
            Example = """
                type: app_closed
                app: obs64.exe
                """,
        },
        new()
        {
            Type = "app_focused",
            Kind = ComponentKind.Trigger,
            Category = AppsCategory,
            Title = "App focused",
            Description = "Fires when a window of the app becomes the active (foreground) window.",
            EventKind = SystemEventKind.AppFocused,
            AvailableIn = ScopeSupport.User,
            Fields =
            [
                AppField("gain focus"),
                Fields.List("title", "Only fire when the window title matches one of these patterns (wildcards allowed, e.g. *YouTube*).", example: "*YouTube*"),
            ],
            Example = """
                type: app_focused
                app: POWERPNT.EXE
                """,
            Notes = "Only available in personal automations: focus belongs to a signed-in user. Switching quickly between windows (for example Alt+Tab) is ignored until the focus has been stable for 250 ms. Switching between windows of the same app does not fire again. The title is checked when focus changes; changing tabs inside the same window does not fire the trigger.",
        },
        new()
        {
            Type = "app_unfocused",
            Kind = ComponentKind.Trigger,
            Category = AppsCategory,
            Title = "App no longer focused",
            Description = "Fires when the app stops being the active window, because another app was focused or the app was closed or minimized.",
            EventKind = SystemEventKind.AppUnfocused,
            AvailableIn = ScopeSupport.User,
            Fields =
            [
                AppField("lose focus"),
                Fields.List("title", "Only fire when the title of the window that lost focus matches one of these patterns.", example: "*YouTube*"),
            ],
            Example = """
                type: app_unfocused
                app: POWERPNT.EXE
                """,
            Notes = "Only available in personal automations. Tip: instead of writing a separate 'unfocused' automation to undo changes, apply a profile on `app_focused`; it is reverted automatically when the app loses focus.",
        },
    ];
}
