using System.Globalization;
using System.Text.RegularExpressions;
using AutoSettings.Core.Catalog;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Engine;

/// <summary>
/// Replaces <c>{{ name }}</c> placeholders in action text fields with details of the event that
/// started the automation, e.g. <c>"Welcome {{ user }}"</c>.
/// </summary>
public static partial class Placeholders
{
    /// <summary>Every supported placeholder with a description (used by the docs and the editor).</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> All =
    [
        ("user", "User name of the user involved (who signed in, or who owns the app), e.g. samet."),
        ("user.domain", "Domain or computer name of that user."),
        ("user.full", "DOMAIN\\name of that user."),
        ("user.sid", "Security identifier (SID) of that user."),
        ("app", "Executable name of the app involved, e.g. chrome.exe."),
        ("app.name", "Executable name without extension, e.g. chrome."),
        ("app.path", "Full path of the app's executable."),
        ("app.pid", "Process id of the app."),
        ("window_title", "Title of the focused window (focus triggers)."),
        ("session", "Windows session number."),
        ("event", "The trigger type that fired, e.g. app_focused, or the plugin event name."),
        ("event.data.<name>", "A value a plugin trigger sent with its event, e.g. {{ event.data.drive }}."),
        ("boot_type", "cold or fast_startup (boot trigger)."),
        ("automation", "Name of the running automation."),
        ("date", "Current date, yyyy-MM-dd."),
        ("time", "Current time, HH:mm:ss."),
        ("now", "Current date and time, yyyy-MM-dd HH:mm:ss."),
    ];

    /// <summary>Builds the placeholder values for a running action.</summary>
    public static IReadOnlyDictionary<string, string?> ValuesFor(ActionContext context, DateTimeOffset now)
    {
        var e = context.Event;
        var user = context.User;
        var process = e?.Process;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = user?.Name,
            ["user.domain"] = user?.Domain,
            ["user.full"] = user?.QualifiedName,
            ["user.sid"] = user?.Sid,
            ["app"] = process?.Name,
            ["app.name"] = process is null ? null : Path.GetFileNameWithoutExtension(process.Name),
            ["app.path"] = process?.Path,
            ["app.pid"] = process?.Id.ToString(CultureInfo.InvariantCulture),
            ["window_title"] = e?.WindowTitle,
            ["session"] = e?.SessionId?.ToString(CultureInfo.InvariantCulture),
            ["event"] = e is null ? null : EventNames.Name(e),
            ["boot_type"] = e?.BootType,
            ["automation"] = context.AutomationName,
            ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            ["now"] = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
        if (e?.Data is { } data)
        {
            foreach (var (key, value) in data)
                values["event.data." + key] = value;
        }
        return values;
    }

    /// <summary>Replaces known placeholders in <paramref name="text"/>. Unknown placeholders are left unchanged.</summary>
    public static string Expand(string text, IReadOnlyDictionary<string, string?> values)
    {
        if (!text.Contains("{{", StringComparison.Ordinal))
            return text;
        return Pattern().Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var value) ? value ?? "" : m.Value);
    }

    /// <summary>Returns a copy of <paramref name="component"/> with placeholders replaced in its text fields.</summary>
    public static ComponentConfig Expand(ComponentConfig component, ComponentDescriptor? descriptor, IReadOnlyDictionary<string, string?> values)
    {
        var copy = component.Clone();
        foreach (var (key, value) in component.Parameters)
        {
            if (value is not string text)
                continue;
            var field = descriptor?.Field(key);
            if (field is null || field.SupportsPlaceholders)
                copy.Parameters[key] = Expand(text, values);
        }
        return copy;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>Maps between <see cref="SystemEventKind"/> and trigger type names.</summary>
public static class EventNames
{
    /// <summary>The trigger type for an event kind, e.g. <c>app_focused</c>.</summary>
    public static string TriggerType(SystemEventKind kind) => kind switch
    {
        SystemEventKind.Boot => "boot",
        SystemEventKind.Logon => "logon",
        SystemEventKind.Logoff => "logoff",
        SystemEventKind.Lock => "lock",
        SystemEventKind.Unlock => "unlock",
        SystemEventKind.AppStarted => "app_started",
        SystemEventKind.AppClosed => "app_closed",
        SystemEventKind.AppFocused => "app_focused",
        SystemEventKind.AppUnfocused => "app_unfocused",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>The name of an event: its trigger type, or the plugin event name for plugin events.</summary>
    public static string Name(SystemEvent e) =>
        e.Kind == SystemEventKind.Plugin && e.PluginEvent is { } name ? name : TriggerType(e.Kind);

    /// <summary>Plain-language description of an event for the activity log.</summary>
    public static string Describe(SystemEvent e)
    {
        var user = e.User is null ? "" : $" ({e.User.QualifiedName})";
        return e.Kind switch
        {
            SystemEventKind.Boot => e.BootType == BootTypes.FastStartup ? "computer started (Fast Startup)" : "computer started",
            SystemEventKind.Logon => $"{e.User?.QualifiedName ?? "a user"} signed in{(e.IsRemote ? " remotely" : "")}",
            SystemEventKind.Logoff => $"{e.User?.QualifiedName ?? "a user"} signed out",
            SystemEventKind.Lock => $"screen locked{user}",
            SystemEventKind.Unlock => $"screen unlocked{user}",
            SystemEventKind.AppStarted => $"{e.Process?.Name ?? "an app"} started",
            SystemEventKind.AppClosed => $"{e.Process?.Name ?? "an app"} closed",
            SystemEventKind.AppFocused => $"{e.Process?.Name ?? "an app"} focused",
            SystemEventKind.AppUnfocused => $"{e.Process?.Name ?? "an app"} lost focus",
            SystemEventKind.Plugin => $"{e.PluginEvent ?? "plugin event"}{user}",
            _ => e.Kind.ToString(),
        };
    }
}
