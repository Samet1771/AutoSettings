namespace AutoSettings.Sdk;

/// <summary>What an action gets when it runs.</summary>
public sealed class ActionRequest
{
    /// <summary>Creates a request. AutoSettings creates these; tests can too.</summary>
    /// <param name="parameters">The action's parameters.</param>
    /// <param name="log">The activity log.</param>
    public ActionRequest(Parameters parameters, IPluginLog log)
    {
        Parameters = parameters;
        Log = log;
    }

    /// <summary>The parameters from the automation, with defaults filled in and placeholders replaced.</summary>
    public Parameters Parameters { get; }

    /// <summary>The event that started the automation, or <c>null</c> for a manual run.</summary>
    public PluginEventInfo? Event { get; init; }

    /// <summary>The name of the automation or profile that runs the action, for messages.</summary>
    public string? AutomationName { get; init; }

    /// <summary>The user the action runs for (the event's user, or the signed-in user).</summary>
    public PluginUser? User { get; init; }

    /// <summary>
    /// When true, the user wants to see what would happen without changing anything. AutoSettings does not call
    /// actions in a dry run today; this is reserved for future use.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>Writes to the activity log.</summary>
    public IPluginLog Log { get; }
}

/// <summary>What a condition gets when it is checked.</summary>
public sealed class ConditionRequest
{
    /// <summary>Creates a request. AutoSettings creates these; tests can too.</summary>
    /// <param name="parameters">The condition's parameters.</param>
    /// <param name="log">The activity log.</param>
    public ConditionRequest(Parameters parameters, IPluginLog log)
    {
        Parameters = parameters;
        Log = log;
    }

    /// <summary>The parameters from the automation, with defaults filled in.</summary>
    public Parameters Parameters { get; }

    /// <summary>The event being handled, or <c>null</c> when the condition is checked for a manual run.</summary>
    public PluginEventInfo? Event { get; init; }

    /// <summary>The signed-in user, when known.</summary>
    public PluginUser? User { get; init; }

    /// <summary>Writes to the activity log.</summary>
    public IPluginLog Log { get; }
}

/// <summary>A Windows user account.</summary>
/// <param name="Name">User name, for example <c>samet</c>.</param>
/// <param name="Domain">Domain or computer name.</param>
/// <param name="Sid">Security identifier.</param>
public sealed record PluginUser(string Name, string? Domain = null, string? Sid = null);

/// <summary>The event that started an automation.</summary>
/// <param name="Name">
/// The event name: a built-in trigger type such as <c>app_focused</c> or <c>unlock</c>, or a plugin event name
/// such as <c>acme.usb.connected</c>.
/// </param>
public sealed record PluginEventInfo(string Name)
{
    /// <summary>The Windows session the event happened in.</summary>
    public int? SessionId { get; init; }

    /// <summary>The user involved.</summary>
    public PluginUser? User { get; init; }

    /// <summary>For app events: the executable name, for example <c>chrome.exe</c>.</summary>
    public string? AppName { get; init; }

    /// <summary>For app events: the full path of the executable, when known.</summary>
    public string? AppPath { get; init; }

    /// <summary>For focus events: the window title.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>For plugin events: the values the plugin sent.</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}
